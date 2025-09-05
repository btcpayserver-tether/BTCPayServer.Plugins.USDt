using System;

namespace BTCPayServer.Plugins.USDt.Configuration.Ethereum;

public class EthUSDtLikeServerSettings
{
    public Uri? JsonRpcUri { get; init; }
    public string? SmartContractAddress { get; init; }
}
