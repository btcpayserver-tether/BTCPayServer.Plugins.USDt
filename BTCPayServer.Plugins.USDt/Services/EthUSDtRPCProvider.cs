using System.Collections.Generic;
using System.Net.Http;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.Ethereum;
using BTCPayServer.Plugins.USDt.Services.Events;
using BTCPayServer.Services;
using NBitcoin;

namespace BTCPayServer.Plugins.USDt.Services;

public class EthUSDtRPCProvider : EVMRPCProvider<EthUSDtLikeConfigurationItem, EthUSDtSettingsChanged>
{
    private readonly USDtPluginConfiguration _usdtPluginConfiguration;
    private readonly EventAggregator _eventAggregator;

    public EthUSDtRPCProvider(
        USDtPluginConfiguration usdtPluginConfiguration,
        EventAggregator eventAggregator,
        SettingsRepository settingsRepository,
        IHttpClientFactory httpClientFactory)
        : base(eventAggregator, settingsRepository, httpClientFactory)
    {
        _usdtPluginConfiguration = usdtPluginConfiguration;
        _eventAggregator = eventAggregator;
    }

    protected override IDictionary<PaymentMethodId, EthUSDtLikeConfigurationItem> GetConfigurationItems()
        => _usdtPluginConfiguration.EthereumUSDtLikeConfigurationItems;

    /// <summary>
    /// Ethereum addresses are already in hex format
    /// </summary>
    protected override string AddressToHex(string address) => address;

    protected override string HexToAddress(string hex) => hex;

    /// <summary>
    /// Ethereum block time is approximately 12 seconds
    /// </summary>
    protected override double GetBlockTimeSeconds() => 12.0;

    protected override void PublishDaemonStateChanged(PaymentMethodId pmi, EVMSummary summary)
    {
        _eventAggregator.Publish(new EthUSDtDaemonStateChanged { Summary = summary, PaymentMethodId = pmi });
    }
}
