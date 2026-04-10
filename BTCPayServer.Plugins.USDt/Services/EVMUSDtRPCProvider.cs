using System.Collections.Generic;
using System.Net.Http;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.EVM;
using BTCPayServer.Services;

namespace BTCPayServer.Plugins.USDt.Services;

public class EVMUSDtRPCProvider : USDtRPCProvider<EVMUSDtLikeConfigurationItem>
{
    private readonly USDtPluginConfiguration _usdtPluginConfiguration;

    public EVMUSDtRPCProvider(USDtPluginConfiguration usdtPluginConfiguration,
        EventAggregator eventAggregator,
        SettingsRepository settingsRepository,
        IHttpClientFactory httpClientFactory)
        : base(eventAggregator, settingsRepository, httpClientFactory)
    {
        _usdtPluginConfiguration = usdtPluginConfiguration;
        Initialize();
    }

    protected override IReadOnlyDictionary<PaymentMethodId, EVMUSDtLikeConfigurationItem> GetConfigurations()
    {
        return _usdtPluginConfiguration.EVMUSDtLikeConfigurationItems;
    }

    public static string ListenerStateSettingKey(EVMUSDtLikeConfigurationItem config)
    {
        return $"{config.GetSettingPrefix()}_LISTENER_STATE";
    }

    protected override string GetListenerStateSettingKey(EVMUSDtLikeConfigurationItem config)
    {
        return ListenerStateSettingKey(config);
    }
}