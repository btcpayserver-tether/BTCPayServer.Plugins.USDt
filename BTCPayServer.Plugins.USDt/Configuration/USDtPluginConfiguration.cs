using System.Collections.Generic;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration.Tron;
using BTCPayServer.Plugins.USDt.Configuration.EVM;

namespace BTCPayServer.Plugins.USDt.Configuration;

public class USDtPluginConfiguration
{
    public Dictionary<PaymentMethodId, TronUSDtLikeConfigurationItem> TronUSDtLikeConfigurationItems { get; init; } = new();
    public Dictionary<PaymentMethodId, EVMUSDtLikeConfigurationItem> EVMUSDtLikeConfigurationItems { get; init; } = new();
}