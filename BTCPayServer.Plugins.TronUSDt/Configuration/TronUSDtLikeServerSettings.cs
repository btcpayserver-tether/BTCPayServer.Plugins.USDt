using System;

namespace BTCPayServer.Plugins.TronUSDt.Configuration;

public class TronUSDtLikeServerSettings
{
    public Uri? JsonRpcUri { get; init; }
    public string? SmartContractAddress { get; init; }
}