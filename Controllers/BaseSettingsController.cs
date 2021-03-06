﻿using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using viafront3.Services;
using viafront3.Models;
using viafront3.Data;
using via_jsonrpc;

namespace viafront3.Controllers
{
    public class BaseSettingsController : BaseController
    {
        protected readonly ExchangeSettings _settings;

        public BaseSettingsController(
          ILogger logger,
          UserManager<ApplicationUser> userManager,
          ApplicationDbContext context,
          IOptions<ExchangeSettings> settings) : base(logger, userManager, context)
        {
            _settings = settings.Value;
        }

        protected async Task<(IdentityResult result, ApplicationUser user)> CreateUser(SignInManager<ApplicationUser> signInManager, IEmailSender emailSender, string username, string email, string password, bool sendEmail, bool signIn)
        {
            var user = new ApplicationUser { UserName = username, Email = email };
            IdentityResult result;
            if (password != null)
                result = await _userManager.CreateAsync(user, password);
            else
                result = await _userManager.CreateAsync(user);
            if (result.Succeeded)
            {
                _logger.LogInformation("Created a new user account.");
                if (email != null && sendEmail)
                {
                    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    var callbackUrl = Url.EmailConfirmationLink(user.Id, code, Request.Scheme);
                    await emailSender.SendEmailConfirmationAsync(email, callbackUrl);
                }

                if (signIn)
                    await signInManager.SignInAsync(user, isPersistent: false);

                if (user.EnsureExchangePresent(_context))
                    _context.SaveChanges();
            }
            return (result, user);
        }

        protected async Task PostUserEmailConfirmed(RoleManager<IdentityRole> roleManager, SignInManager<ApplicationUser> signInManager, KycSettings kycSettings, ApplicationUser user)
        {
            // add email confirmed role
            var role = await roleManager.FindByNameAsync(Utils.EmailConfirmedRole);
            System.Diagnostics.Debug.Assert(role != null);
            if (!await _userManager.IsInRoleAsync(user, role.Name))
                await _userManager.AddToRoleAsync(user, role.Name);

            // refresh users cookie (so they dont have to log out/ log in)
            await signInManager.RefreshSignInAsync(user);

            // grant email kyc
            for (var i = 0; i < kycSettings.Levels.Count(); i++)
            {
                if (kycSettings.Levels[i].Name == "Email Confirmed")
                {
                    user.UpdateKyc(_logger, _context, kycSettings, i);
                    _context.SaveChanges();
                    break;
                }
            }
        }
    }

    public class BaseWalletController : BaseSettingsController
    {
        protected readonly IWalletProvider _walletProvider;
        protected readonly KycSettings _kycSettings;

        public BaseWalletController(
          ILogger logger,
          UserManager<ApplicationUser> userManager,
          ApplicationDbContext context,
          IOptions<ExchangeSettings> settings,
          IWalletProvider walletProvider,
          IOptions<KycSettings> kycSettings) : base(logger, userManager, context, settings)
        {
            _walletProvider = walletProvider;
            _kycSettings = kycSettings.Value;
        }

        protected decimal CalculateWithdrawalAssetEquivalent(ILogger logger, KycSettings kyc, string asset, decimal amount)
        {
            if (asset == kyc.WithdrawalAsset)
                return amount;

            foreach (var market in _settings.Markets.Keys)
            {
                if (market.StartsWith(asset) && market.EndsWith(kyc.WithdrawalAsset))
                {
                    try
                    {
                        //TODO: move this to a ViaRpcProvider in /Services (like IWalletProvider)
                        var via = new ViaJsonRpc(_settings.AccessHttpUrl);
                        var price = via.MarketPriceQuery(market);
                        var priceDec = decimal.Parse(price);
                        if (priceDec <= 0)
                            continue;
                        return amount * priceDec;
                    }
                    catch (ViaJsonException ex)
                    {
                        _logger.LogError(ex, $"Error getting market price for asset '{market}'");
                    }
                }
            };

            if (kyc.WithdrawalAssetBaseRates.ContainsKey(asset))
                return amount * kyc.WithdrawalAssetBaseRates[asset];

            logger.LogError($"no price found for asset {asset}");
            throw new Exception($"no price found for asset {asset}");
        }

        protected (bool success, decimal withdrawalAssetAmount, string error) ValidateWithdrawlLimit(ApplicationUser user, string asset, decimal amount)
        {
            var withdrawalTotalThisPeriod = user.WithdrawalTotalThisPeriod(_kycSettings);
            var withdrawalAssetAmount = CalculateWithdrawalAssetEquivalent(_logger, _kycSettings, asset, amount);
            var newWithdrawalTotal = withdrawalTotalThisPeriod + withdrawalAssetAmount;
            var kycLevel = _kycSettings.Levels[0];
            if (user.Kyc != null && user.Kyc.Level < _kycSettings.Levels.Count)
                kycLevel = _kycSettings.Levels[user.Kyc.Level];
            if (decimal.Parse(kycLevel.WithdrawalLimit) <= newWithdrawalTotal)
            {
                var withdrawalTotalThisPeriodString = _walletProvider.AmountToString(_kycSettings.WithdrawalAsset, withdrawalTotalThisPeriod);
                if (withdrawalTotalThisPeriodString == null)
                    withdrawalTotalThisPeriodString = withdrawalTotalThisPeriod.ToString();
                return (false, 0,
                    $"Your withdrawal limit is {kycLevel.WithdrawalLimit} {_kycSettings.WithdrawalAsset} equivalent, your current withdrawal total this period ({_kycSettings.WithdrawalPeriod}) is {withdrawalTotalThisPeriodString} {_kycSettings.WithdrawalAsset}");
            }
            return (true, withdrawalAssetAmount, null);
        }
    }
}
