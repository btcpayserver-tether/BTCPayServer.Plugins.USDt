using System.Collections.Generic;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.Tron;
using BTCPayServer.Plugins.USDt.Services.Events;

namespace BTCPayServer.Plugins.USDt.Services;

public class TronUSDtLikeSummaryUpdaterHostedService(
    TronUSDtRPCProvider tronUSDtRpcProvider,
    USDtPluginConfiguration usdtPluginConfiguration,
    Logs logs)
    : EVMSummaryUpdaterHostedService<TronUSDtLikeConfigurationItem, TronUSDtRPCProvider, TronUSDtSettingsChanged>(
        tronUSDtRpcProvider, logs)
{
    protected override string ChainName => "TronUSDt-like";

    protected override IDictionary<PaymentMethodId, TronUSDtLikeConfigurationItem> GetConfigurationItems()
        => usdtPluginConfiguration.TronUSDtLikeConfigurationItems;
}
