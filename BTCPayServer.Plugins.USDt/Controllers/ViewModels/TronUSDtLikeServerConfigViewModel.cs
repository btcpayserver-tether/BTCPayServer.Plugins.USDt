using System;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.Tron;
using BTCPayServer.Validation;

namespace BTCPayServer.Plugins.USDt.Controllers.ViewModels;

public class TronUSDtLikeServerConfigViewModel
{
    [TronBase58]
    public string? SmartContractAddress { get; init; }
    public string? DefaultSmartContractAddress { get; set; }

    [Uri]
    public string? JsonRpcUri { get; init; }
    public Uri? DefaultJsonRpcUri { get; set; }
}