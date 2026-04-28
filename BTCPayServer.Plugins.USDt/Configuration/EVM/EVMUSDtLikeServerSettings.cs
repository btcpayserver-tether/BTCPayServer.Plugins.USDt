using System;

namespace BTCPayServer.Plugins.USDt.Configuration.EVM;

public class EVMUSDtLikeServerSettings
{
    public Uri? JsonRpcUri { get; init; }
    public string? SmartContractAddress { get; init; }
}
