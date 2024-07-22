using System;

namespace BTCPayServer.Plugins.TronUSDT.Configuration;

public class TronUSDTLikeConfigurationItem
{
    public required Uri JsonRpcUri { get; init; }
    public required string SmartContractAddress { get; init; }
}