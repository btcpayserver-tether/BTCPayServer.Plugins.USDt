using System.Collections.Generic;
using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.USDt.Configuration;

public class USDtPluginConfiguration
{
    public Dictionary<string, TronUSDtLikeConfigurationItem> TronUSDtLikeConfigurationItems { get; set; } = [];
}