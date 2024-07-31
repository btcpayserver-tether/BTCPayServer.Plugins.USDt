using System;

namespace BTCPayServer.Plugins.TronUSDT.Controllers.ViewModels;

public class TronUSDTLikeServerConfigViewModel
{
    public string? SmartContractAddress { get; init; }
    public required string DefaultSmartContractAddress { get; init; }

    public string? JsonRpcUri { get; init; }
    public required Uri DefaultJsonRpcUri { get; init; }
}