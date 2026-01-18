using System.Collections.Generic;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.Ethereum;
using BTCPayServer.Plugins.USDt.Services.Events;

namespace BTCPayServer.Plugins.USDt.Services;

public class EthUSDtLikeSummaryUpdaterHostedService(
    EthUSDtRPCProvider rpcProvider,
    USDtPluginConfiguration usdtPluginConfiguration,
    Logs logs)
    : EVMSummaryUpdaterHostedService<EthUSDtLikeConfigurationItem, EthUSDtRPCProvider, EthUSDtSettingsChanged>(
        rpcProvider, logs)
{
    protected override string ChainName => "Ethereum USDt-like";

    protected override IDictionary<PaymentMethodId, EthUSDtLikeConfigurationItem> GetConfigurationItems()
        => usdtPluginConfiguration.EthereumUSDtLikeConfigurationItems;
}
