using System;
using BTCPayServer.Plugins.TronUSDt.Configuration;
using BTCPayServer.Validation;

namespace BTCPayServer.Plugins.TronUSDt.Controllers.ViewModels;

public class TronUSDtLikeServerConfigViewModel
{
    [TronBase58]
    public string? SmartContractAddress { get; init; }
    public string? DefaultSmartContractAddress { get; set; }

    [Uri]
    public string? JsonRpcUri { get; init; }
    public Uri? DefaultJsonRpcUri { get; set; }
}