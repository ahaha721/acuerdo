
using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Identity;
using RestSharp;
using Newtonsoft.Json;
using viafront3.Data;
using viafront3.Models;

namespace viafront3
{
    public static class RestUtils
    {
        public static string HMacWithSha256(string secret, string message)
        {
            using (var hmac = new HMACSHA256(ASCIIEncoding.ASCII.GetBytes(secret)))
            {
                var hash = hmac.ComputeHash(ASCIIEncoding.ASCII.GetBytes(message));
                return Convert.ToBase64String(hash);
            }
        }

        public static IRestResponse ServiceRequest(string url, string endpoint, string secret, string jsonBody)
        {
            var client = new RestClient(url);
            var request = new RestRequest(endpoint, Method.POST);
            request.RequestFormat = DataFormat.Json;
            request.AddParameter("application/json", jsonBody, ParameterType.RequestBody);
            if (secret != null)
            {
                var sig = HMacWithSha256(secret, jsonBody);
                request.AddHeader("X-Signature", sig);
            }
            var response = client.Execute(request);
            return response;
        }

        public static IRestResponse ServiceRequest(string url, string endpoint, string jsonBody)
        {
            return ServiceRequest(url, endpoint, null, jsonBody);
        }

        public static viafront3.Models.ApiViewModels.ApiFiatPaymentRequest CreateFiatPaymentRequest(ILogger logger, FiatProcessorSettings fiatSettings, string token, string asset, decimal amount, long expiry)
        {
            // call payment server to create request
            var amount_cents =  Convert.ToInt32(amount * 100);
            var jsonBody = JsonConvert.SerializeObject(new { api_key = fiatSettings.FiatServerApiKey, token = token, asset = asset, amount = amount_cents, return_url = "", expiry = expiry });
            var response = RestUtils.ServiceRequest(fiatSettings.FiatServerUrl, "payment_create", fiatSettings.FiatServerSecret, jsonBody);
            if (response.IsSuccessful)
            {
                var json = JsonConvert.DeserializeObject<Dictionary<string, string>>(response.Content);
                if (json.ContainsKey("status"))
                {
                    var status = json["status"];
                    // return to user
                    var model = new viafront3.Models.ApiViewModels.ApiFiatPaymentRequest
                    {
                        Token = token,
                        ServiceUrl = $"{fiatSettings.FiatServerUrl}/payment/{token}",
                        Status = status,
                        Asset = asset,
                        Amount = amount,
                    };
                    return model;
                }
            }
            else
                logger.LogError($"fiat payment request ({fiatSettings.FiatServerUrl}) failed with http statuscode: {response.StatusCode}");
            return null;
        }

        public static viafront3.Models.ApiViewModels.ApiFiatPaymentRequest GetFiatPaymentRequest(FiatProcessorSettings fiatSettings, string token)
        {
            var jsonBody = JsonConvert.SerializeObject(new { api_key = fiatSettings.FiatServerApiKey, token = token });
            var response = RestUtils.ServiceRequest(fiatSettings.FiatServerUrl, "payment_status", fiatSettings.FiatServerSecret, jsonBody);
            if (response.IsSuccessful)
            {
                var json = JsonConvert.DeserializeObject<Dictionary<string, string>>(response.Content);
                if (json.ContainsKey("status"))
                {
                    var status = json["status"];
                    var asset = json["asset"];
                    var amount = json["amount"];
                    // return to user
                    var model = new viafront3.Models.ApiViewModels.ApiFiatPaymentRequest
                    {
                        Token = token,
                        ServiceUrl = $"{fiatSettings.FiatServerUrl}/request/{token}",
                        Status = status,
                        Asset = asset,
                        Amount = decimal.Parse(amount),
                    };
                    return model;
                }
            }
            return null;
        }


        public static  viafront3.Models.ApiViewModels.ApiFiatPayoutRequest CreateFiatPayoutRequest(ILogger logger, ExchangeSettings settings, FiatProcessorSettings fiatSettings, string token, string asset, decimal amount, string account_number, string email)
        {
            var cents = amount * Utils.IntPow(10, settings.Assets[asset].Decimals);
            var centsInt = Convert.ToInt64(cents);

            // call payment server to create request
            var jsonBody = JsonConvert.SerializeObject(new { api_key = fiatSettings.FiatServerApiKey, token = token, asset = asset, amount = centsInt, account_number = account_number, account_name = email, reference = fiatSettings.PayoutsReference, code = token });
            var response = ServiceRequest(fiatSettings.FiatServerUrl, "payout_create", fiatSettings.FiatServerSecret, jsonBody);
            if (response.IsSuccessful)
            {
                var json = JsonConvert.DeserializeObject<Dictionary<string, string>>(response.Content);
                if (json.ContainsKey("status"))
                {
                    var status = json["status"];
                    // return to user
                    var model = new viafront3.Models.ApiViewModels.ApiFiatPayoutRequest
                    {
                        Token = token,
                        Status = status,
                        Asset = asset,
                        Amount = amount,
                    };
                    return model;
                }
            }
            else
                logger.LogError($"fiat payment request ({fiatSettings.FiatServerUrl}) failed with http statuscode: {response.StatusCode}");
            return null;
        }

        public static viafront3.Models.ApiViewModels.ApiFiatPayoutRequest GetFiatPayoutRequest(FiatProcessorSettings fiatSettings, string token)
        {
            var jsonBody = JsonConvert.SerializeObject(new { api_key = fiatSettings.FiatServerApiKey, token = token });
            var response = RestUtils.ServiceRequest(fiatSettings.FiatServerUrl, "payout_status", fiatSettings.FiatServerSecret, jsonBody);
            if (response.IsSuccessful)
            {
                var json = JsonConvert.DeserializeObject<Dictionary<string, string>>(response.Content);
                if (json.ContainsKey("status"))
                {
                    var status = json["status"];
                    var asset = json["asset"];
                    var amount = json["amount"];
                    // return to user
                    var model = new viafront3.Models.ApiViewModels.ApiFiatPayoutRequest
                    {
                        Token = token,
                        Status = status,
                        Asset = asset,
                        Amount = decimal.Parse(amount),
                    };
                    return model;
                }
            }
            return null;
        }

        public static bool CheckBankAccount(FiatProcessorSettings fiatSettings, string bankaccount)
        {
            var jsonBody = JsonConvert.SerializeObject(new { account = bankaccount });
            var response = RestUtils.ServiceRequest(fiatSettings.FiatServerUrl, "bankaccount_is_valid", null, jsonBody);
            if (response.IsSuccessful)
            {
                var json = JsonConvert.DeserializeObject<Dictionary<string, object>>(response.Content);
                if (json.ContainsKey("result") && json["result"] is bool)
                {
                    return (bool)json["result"];
                }
            }
            return true;
        }

        public static async Task<Models.ApiViewModels.ApiAccountKycRequest> CreateKycRequest(ILogger logger, ApplicationDbContext context,
            UserManager<ApplicationUser> userManager, KycSettings kycSettings, string applicationUserId, string email)
        {
            // check request does not already exist
            var kycReq = context.KycRequests.Where(r => r.ApplicationUserId == applicationUserId).FirstOrDefault();
            if (kycReq != null)
                return await CheckKycRequest(logger, context, userManager, kycSettings, applicationUserId, kycReq.Token);
            // call kyc server to create request
            var token = Utils.CreateToken();
            var jsonBody = JsonConvert.SerializeObject(new { api_key = kycSettings.KycServerApiKey, token = token, email = email });
            var response = RestUtils.ServiceRequest(kycSettings.KycServerUrl, "request", kycSettings.KycServerApiSecret, jsonBody);
            if (response.IsSuccessful)
            {
                var json = JsonConvert.DeserializeObject<Dictionary<string, string>>(response.Content);
                if (json.ContainsKey("status"))
                {
                    var status = json["status"];
                    // save to database
                    var date = DateTimeOffset.Now.ToUnixTimeSeconds();
                    kycReq = new KycRequest { ApplicationUserId = applicationUserId, Date = date, Token = token };
                    context.KycRequests.Add(kycReq);
                    context.SaveChanges();
                    // return to user
                    var model = new viafront3.Models.ApiViewModels.ApiAccountKycRequest
                    {
                        Token = token,
                        ServiceUrl = $"{kycSettings.KycServerUrl}/request/{token}",
                        Status = status,
                    };
                    return model;
                }
            }
            else
                logger.LogError($"kyc request ({kycSettings.KycServerUrl}) failed with http statuscode: {response.StatusCode}");
            return null;
        }

        public static async Task<Models.ApiViewModels.ApiAccountKycRequest> CheckKycRequest(ILogger logger, ApplicationDbContext context, 
            UserManager<ApplicationUser> userManager, KycSettings kycSettings, string applicationUserId, string token)
        {
            var jsonBody = JsonConvert.SerializeObject(new { token = token });
            var response = RestUtils.ServiceRequest(kycSettings.KycServerUrl, "status", jsonBody);
            if (response.IsSuccessful)
            {
                var json = JsonConvert.DeserializeObject<Dictionary<string, string>>(response.Content);
                if (json.ContainsKey("status"))
                {
                    var status = json["status"];
                    // update kyc level if complete
                    if (status.ToLower() == viafront3.Models.ApiViewModels.ApiRequestStatus.Completed.ToString().ToLower())
                    {
                        var newLevel = 2;
                        var user = await userManager.FindByIdAsync(applicationUserId);
                        if (user == null)
                            return null;
                        var userKyc = user.Kyc;
                        if (userKyc == null)
                        {
                            userKyc = new Kyc { ApplicationUserId = user.Id, Level = newLevel };
                            context.Kycs.Add(userKyc);
                        }
                        else if (userKyc.Level < newLevel)
                        {

                            userKyc.Level = newLevel;
                            context.Kycs.Update(userKyc);
                        }
                        context.SaveChanges();
                    }
                    // return to user
                    var model = new viafront3.Models.ApiViewModels.ApiAccountKycRequest
                    {
                        Token = token,
                        ServiceUrl = $"{kycSettings.KycServerUrl}/request/{token}",
                        Status = status,
                    };
                    return model;
                }
            }
            return null;
        }
    }
}