using System.Collections.Generic;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration.Tron;

namespace BTCPayServer.Plugins.USDt.Configuration;

public class USDtPluginConfiguration
{
    public Dictionary<PaymentMethodId, TronUSDtLikeConfigurationItem> TronUSDtLikeConfigurationItems { get; init; } = [];
}