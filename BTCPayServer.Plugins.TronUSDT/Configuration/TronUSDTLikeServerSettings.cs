using System;

namespace BTCPayServer.Plugins.TronUSDT.Configuration;

public class TronUSDTLikeServerSettings
{
    public Uri? JsonRpcUri { get; init; }
    public string? SmartContractAddress { get; init; }
}