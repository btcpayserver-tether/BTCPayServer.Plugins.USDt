using System;

namespace BTCPayServer.Plugins.USDt.Configuration;

public class TronUSDtLikeServerSettings
{
    public Uri? JsonRpcUri { get; init; }
    public string? SmartContractAddress { get; init; }
}