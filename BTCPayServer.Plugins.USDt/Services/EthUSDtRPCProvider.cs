using System.Collections.Generic;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.Ethereum;
using BTCPayServer.Services;

namespace BTCPayServer.Plugins.USDt.Services;

public class EthUSDtRPCProvider : USDtRPCProvider<EthUSDtLikeConfigurationItem>
{
    private readonly USDtPluginConfiguration _usdtPluginConfiguration;

    public EthUSDtRPCProvider(USDtPluginConfiguration usdtPluginConfiguration,
        EventAggregator eventAggregator,
        SettingsRepository settingsRepository,
        IHttpClientFactory httpClientFactory)
        : base(eventAggregator, settingsRepository, httpClientFactory)
    {
        _usdtPluginConfiguration = usdtPluginConfiguration;
        Initialize();
    }

    protected override IReadOnlyDictionary<PaymentMethodId, EthUSDtLikeConfigurationItem> GetConfigurations()
    {
        return _usdtPluginConfiguration.EVMUSDtLikeConfigurationItems;
    }

    public static string ListenerStateSettingKey(EthUSDtLikeConfigurationItem config)
    {
        return $"{config.GetSettingPrefix()}_LISTENER_STATE";
    }

    protected override string GetListenerStateSettingKey(EthUSDtLikeConfigurationItem config)
    {
        return ListenerStateSettingKey(config);
    }
}