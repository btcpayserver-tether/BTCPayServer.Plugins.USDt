using System.Collections.Generic;
using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.USDt.Configuration;

public class TronUSDtLikeConfiguration
{
    public Dictionary<string, TronUSDtLikeConfigurationItem> TronUSDtLikeConfigurationItems { get; set; } = [];
}