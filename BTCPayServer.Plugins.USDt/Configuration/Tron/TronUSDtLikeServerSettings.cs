using System;

namespace BTCPayServer.Plugins.USDt.Configuration.Tron;

public class TronUSDtLikeServerSettings
{
    public Uri? JsonRpcUri { get; init; }
    public string? SmartContractAddress { get; init; }
}