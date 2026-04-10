using System.Collections.Generic;
using System.Net.Http;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.Tron;
using BTCPayServer.Services;

namespace BTCPayServer.Plugins.USDt.Services;

public class TronUSDtRPCProvider : USDtRPCProvider<TronUSDtLikeConfigurationItem>
{
    private readonly USDtPluginConfiguration _usdtPluginConfiguration;

    public TronUSDtRPCProvider(USDtPluginConfiguration usdtPluginConfiguration,
        EventAggregator eventAggregator,
        SettingsRepository settingsRepository,
        IHttpClientFactory httpClientFactory)
        : base(eventAggregator, settingsRepository, httpClientFactory)
    {
        _usdtPluginConfiguration = usdtPluginConfiguration;
        Initialize();
    }

    protected override IReadOnlyDictionary<PaymentMethodId, TronUSDtLikeConfigurationItem> GetConfigurations()
    {
        return _usdtPluginConfiguration.TronUSDtLikeConfigurationItems;
    }

    public static string ListenerStateSettingKey(TronUSDtLikeConfigurationItem config)
    {
        return $"{config.GetSettingPrefix()}_LISTENER_STATE";
    }

    protected override string GetListenerStateSettingKey(TronUSDtLikeConfigurationItem config)
    {
        return ListenerStateSettingKey(config);
    }

    protected override HttpClient CreateHttpClient(PaymentMethodId paymentMethodId, TronUSDtLikeConfigurationItem config)
    {
        var httpClient = base.CreateHttpClient(paymentMethodId, config);

        if (config.HttpHeaders == null)
            return httpClient;

        foreach (var header in config.HttpHeaders)
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }

        return httpClient;
    }

    protected override string NormalizeAddress(string address, TronUSDtLikeConfigurationItem config)
    {
        return TronUSDtAddressHelper.Base58ToHex(address);
    }

    protected override string GetTokenContractAddress(TronUSDtLikeConfigurationItem config)
    {
        return TronUSDtAddressHelper.Base58ToHex(config.SmartContractAddress);
    }
}