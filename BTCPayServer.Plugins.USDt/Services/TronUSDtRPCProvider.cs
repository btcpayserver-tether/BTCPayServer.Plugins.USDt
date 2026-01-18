using System.Collections.Generic;
using System.Net.Http;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.Tron;
using BTCPayServer.Plugins.USDt.Services.Events;
using BTCPayServer.Services;
using NBitcoin;

namespace BTCPayServer.Plugins.USDt.Services;

public class TronUSDtRPCProvider : EVMRPCProvider<TronUSDtLikeConfigurationItem, TronUSDtSettingsChanged>
{
    private readonly USDtPluginConfiguration _usdtPluginConfiguration;
    private readonly EventAggregator _eventAggregator;

    public TronUSDtRPCProvider(
        USDtPluginConfiguration usdtPluginConfiguration,
        EventAggregator eventAggregator,
        SettingsRepository settingsRepository,
        IHttpClientFactory httpClientFactory)
        : base(eventAggregator, settingsRepository, httpClientFactory)
    {
        _usdtPluginConfiguration = usdtPluginConfiguration;
        _eventAggregator = eventAggregator;
    }

    protected override IDictionary<PaymentMethodId, TronUSDtLikeConfigurationItem> GetConfigurationItems()
        => _usdtPluginConfiguration.TronUSDtLikeConfigurationItems;

    protected override string AddressToHex(string address)
        => TronUSDtAddressHelper.Base58ToHex(address);

    protected override string HexToAddress(string hex)
        => TronUSDtAddressHelper.HexToBase58(hex);

    /// <summary>
    /// Tron block time is approximately 3 seconds
    /// </summary>
    protected override double GetBlockTimeSeconds() => 3.0;

    protected override void PublishDaemonStateChanged(PaymentMethodId pmi, EVMSummary summary)
    {
        _eventAggregator.Publish(new TronUSDtDaemonStateChanged { Summary = summary, PaymentMethodId = pmi });
    }
}
