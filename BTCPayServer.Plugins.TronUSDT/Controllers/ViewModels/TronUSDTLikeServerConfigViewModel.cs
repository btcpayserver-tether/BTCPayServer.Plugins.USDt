using System;
using BTCPayServer.Plugins.TronUSDT.Configuration;
using BTCPayServer.Validation;

namespace BTCPayServer.Plugins.TronUSDT.Controllers.ViewModels;

public class TronUSDTLikeServerConfigViewModel
{
    [TronBase58]
    public string? SmartContractAddress { get; init; }
    public string? DefaultSmartContractAddress { get; set; }

    [Uri]
    public string? JsonRpcUri { get; init; }
    public Uri? DefaultJsonRpcUri { get; set; }
}