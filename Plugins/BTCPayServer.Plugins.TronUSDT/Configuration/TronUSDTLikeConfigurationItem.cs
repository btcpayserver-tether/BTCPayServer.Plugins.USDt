using System;

namespace BTCPayServer.Plugins.TronUSDT.Configuration;

public class TronUSDTLikeConfigurationItem
{
    public Uri JsonRpcUri { get; set; }
    public string SmartContractAddress { get; set; }
}