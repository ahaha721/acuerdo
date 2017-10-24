﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using viafront3.Models;
using via_jsonrpc;

namespace viafront3.Controllers
{
    [Route("[controller]/[action]")]
    public class MarketController : Controller
    {
        private readonly ExchangeSettings _settings;

        public MarketController(IOptions<ExchangeSettings> settings)
        {
            _settings = settings.Value;
        }

        public IActionResult Orderbook()
        {
            var via = new ViaJsonRpc(_settings.AccessHttpHost);
            var orderDepth = via.OrderDepthQuery(_settings.Market, _settings.OrderBookLimit, _settings.OrderBookInterval);

            ViewData["Market"] = string.Format("{0}/{1}", _settings.MarketAmountUnit, _settings.MarketPriceUnit);
            ViewData["AmountUnit"] = _settings.MarketAmountUnit;
            ViewData["PriceUnit"] = _settings.MarketPriceUnit;
            ViewData["OrderDepth"] = orderDepth;

            return View();
        }
    }
}
