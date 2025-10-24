using System;
using System.Collections.Generic;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Configuration;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.Tron;
using BTCPayServer.Plugins.USDt.Configuration.Ethereum;
using BTCPayServer.Plugins.USDt.Controllers;
using BTCPayServer.Plugins.USDt.Services;
using BTCPayServer.Plugins.USDt.Services.Payments;
using BTCPayServer.Services.Rates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.Plugins.USDt;

public class USDtPlugin : BaseBTCPayServerPlugin
{
    public const string PluginNavKey = "USDtPlugin";
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new IBTCPayServerPlugin.PluginDependency { Identifier = nameof(BTCPayServer), Condition = ">=2.0.0" }
    ];

    public override void Execute(IServiceCollection serviceCollection)
    {
        RegisterServices(serviceCollection);
        base.Execute(serviceCollection);
    }

    private void RegisterServices(IServiceCollection services)
    {
        var networkProvider = ((PluginServiceCollection)services).BootstrapServices.GetRequiredService<NBXplorerNetworkProvider>();
        var configuration = ((PluginServiceCollection)services).BootstrapServices.GetRequiredService<IConfiguration>();
        var settingsRepository = services.BuildServiceProvider().GetService<ISettingsRepository>() ??
                                 throw new InvalidOperationException("serviceProvider.GetService<ISettingsRepository>()");

        var tronUSDtConfiguration = GetTronUSDtLikeDefaultConfigurationItem(networkProvider, configuration);
        tronUSDtConfiguration = OverrideWithServerSettings(tronUSDtConfiguration, settingsRepository);

        // Prepare Ethereum USDt configuration only on Mainnet for initial scope
        var ethUsdtConfiguration = GetEthUSDtLikeDefaultConfigurationItem(networkProvider, configuration);
        ethUsdtConfiguration = OverrideWithServerSettings(ethUsdtConfiguration, settingsRepository);

        var pluginConfiguration = new USDtPluginConfiguration
        {
            TronUSDtLikeConfigurationItems = new Dictionary<PaymentMethodId, TronUSDtLikeConfigurationItem>
            {
                { tronUSDtConfiguration.GetPaymentMethodId(), tronUSDtConfiguration }
            },
            EthereumUSDtLikeConfigurationItems = new Dictionary<PaymentMethodId, EthUSDtLikeConfigurationItem>()
            {
                { ethUsdtConfiguration.GetPaymentMethodId(), ethUsdtConfiguration}
            }
        };

        services.AddSingleton(pluginConfiguration);

        services.AddCurrencyData(new CurrencyData
        {
            Code = Constants.USDtCurrency,
            Name = Constants.USDtCurrencyDisplayName,
            Divisibility = 6,
            Symbol = Constants.USDtCurrencyDisplayName,
            Crypto = true
        });

        var tronUSDtPaymentMethodId = tronUSDtConfiguration.GetPaymentMethodId();
        services.AddTransactionLinkProvider(tronUSDtPaymentMethodId, new TronUSDtTransactionLinkProvider(tronUSDtConfiguration.BlockExplorerLink));
        services.AddSingleton<TronUSDtRPCProvider>();
        services.AddHostedService<TronUSDtLikeSummaryUpdaterHostedService>();
        services.AddHostedService<TronUSDtListener>();

        services.AddSingleton(new DefaultRules(tronUSDtConfiguration.DefaultRateRules));

        services.AddSingleton(provider => (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider, typeof(TronUSDtLikePaymentMethodHandler),
            tronUSDtConfiguration));
        services.AddSingleton<IPaymentLinkExtension>(provider =>
            (IPaymentLinkExtension)ActivatorUtilities.CreateInstance(provider, typeof(TronUSDtPaymentLinkExtension), tronUSDtPaymentMethodId));
        services.AddSingleton(provider =>
            (ICheckoutModelExtension)ActivatorUtilities.CreateInstance(provider, typeof(TronUSDtCheckoutModelExtension),
                tronUSDtConfiguration));
        
        services.AddDefaultPrettyName(tronUSDtPaymentMethodId, tronUSDtConfiguration.DisplayName);

        // For future usages (multiple TRC20)
        //services.AddSingleton<IUIExtension>(new UIExtension("TronUSDt/StoreNavTronUSDtExtension", "store-integrations-nav"));
        services.AddUIExtension("server-nav","TronUSDtLike/ServerNavTronUSDtExtension");
        services.AddUIExtension("store-wallets-nav", "TronUSDtLike/StoreWalletsNavTronUSDtExtension");
        services.AddUIExtension("store-invoices-payments", "TronUSDtLike/ViewTronUSDtLikePaymentData");
        services.AddUIExtension("checkout-payment-method", "EmptyCheckoutPaymentMethodExtension");
        services.AddSingleton<ISyncSummaryProvider, TronUSDtSyncSummaryProvider>();
        
        // Ethereum USDt wiring
        var ethPaymentMethodId = ethUsdtConfiguration.GetPaymentMethodId();
        services.AddTransactionLinkProvider(ethPaymentMethodId, new EthUSDtTransactionLinkProvider(ethUsdtConfiguration.BlockExplorerLink));
        services.AddSingleton<EthUSDtRPCProvider>();
        services.AddHostedService<EthUSDtLikeSummaryUpdaterHostedService>();
        services.AddHostedService<EthUSDtListener>();

        services.AddSingleton(provider => (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider, typeof(EthUSDtPaymentMethodHandler),
            ethUsdtConfiguration));
        services.AddSingleton<IPaymentLinkExtension>(provider =>
            (IPaymentLinkExtension)ActivatorUtilities.CreateInstance(provider, typeof(EthUSDtPaymentLinkExtension), ethPaymentMethodId, ethUsdtConfiguration.SmartContractAddress, ethUsdtConfiguration.Divisibility));
        services.AddSingleton(provider =>
            (ICheckoutModelExtension)ActivatorUtilities.CreateInstance(provider, typeof(EthUSDtCheckoutModelExtension),
                ethUsdtConfiguration));

        services.AddDefaultPrettyName(ethPaymentMethodId, ethUsdtConfiguration.DisplayName);

        services.AddSingleton<ISyncSummaryProvider, EthUSDtSyncSummaryProvider>();

        // Store UI extensions for ETH (addresses management & checkout)
        services.AddUIExtension("store-wallets-nav", "EthUSDtLike/StoreWalletsNavEthUSDtExtension");
        services.AddUIExtension("checkout-payment-method", "EmptyCheckoutPaymentMethodExtension");

        // Server settings navigation
        services.AddUIExtension("server-nav","EthUSDtLike/ServerNavEthUSDtExtension");

        services.AddSingleton<ISwaggerProvider, SwaggerProvider>();
    }


    private static TronUSDtLikeConfigurationItem GetTronUSDtHardcodedConfig(ChainName chainName)
    {
        const string logo =
            "data:image/svg+xml,%3Csvg width='165' height='165' viewBox='0 0 165 165' fill='none' xmlns='http://www.w3.org/2000/svg'%3E%3Ccircle cx='82.5' cy='82.5' r='82.5' fill='%23019493'/%3E%3Cpath d='M128.225 75.7991L112.84 49.382C112.209 48.3018 111.049 47.6387 109.787 47.6387H56.9688C55.7442 47.6387 54.6051 48.2697 53.9634 49.2964L37.5196 75.7724C36.664 77.152 36.8832 78.9274 38.0436 80.0665L80.3431 121.355C81.7121 122.697 83.9206 122.697 85.295 121.355L127.648 80.013C128.787 78.9007 129.022 77.1734 128.225 75.8044V75.7991ZM106.477 83.1306C106.418 85.8365 98.4875 88.0879 87.9206 88.6387V101.254H77.9313V88.6387C67.3645 88.0879 59.4394 85.8365 59.3805 83.1306V77.4943C59.4394 74.7884 67.3645 72.5317 77.9313 71.9809V66.8419H62.8886V59.4301H102.969V66.8419H87.926V71.9809C98.4928 72.5317 106.423 74.7884 106.482 77.4943V83.1306H106.477Z' fill='white'/%3E%3Cpath d='M87.9206 81.3659C86.311 81.4515 84.6372 81.4889 82.926 81.4889C81.2148 81.4889 79.541 81.4462 77.9313 81.3659V75.9809C68.9527 76.4462 61.8725 78.1467 59.9206 80.3071C62.2255 82.8526 71.6533 84.7617 82.9313 84.7617C94.2094 84.7617 103.632 82.8526 105.937 80.3071C103.979 78.1467 96.91 76.4462 87.926 75.9809V81.3659H87.9206Z' fill='white'/%3E%3Cpath d='M113 127C123.497 127 132 118.715 132 108.5C132 98.2847 123.497 90 113 90C102.503 90 94 98.2847 94 108.5C94 118.715 102.51 127 113 127Z' fill='%23019493'/%3E%3Cpath d='M113 125C122.392 125 130 117.611 130 108.5C130 99.389 122.392 92 113 92C103.608 92 96 99.389 96 108.5C96 117.611 103.614 125 113 125Z' fill='white'/%3E%3Cpath d='M122.727 105.425C121.792 104.644 120.493 103.454 119.441 102.612L119.376 102.575C119.273 102.502 119.156 102.441 119.032 102.398C116.487 101.971 104.643 99.9758 104.416 100C104.351 100.006 104.286 100.031 104.234 100.061L104.175 100.104C104.104 100.171 104.045 100.25 104.013 100.342L104 100.379V100.58V100.61C105.331 103.954 110.597 114.9 111.636 117.475C111.701 117.652 111.818 117.982 112.039 118H112.091C112.208 118 112.714 117.396 112.714 117.396C112.714 117.396 121.76 107.524 122.675 106.474C122.792 106.346 122.896 106.206 122.987 106.059C123.013 105.943 123 105.827 122.954 105.717C122.909 105.608 122.824 105.504 122.727 105.425ZM115.026 106.578L118.883 103.698L121.149 105.577L115.026 106.578ZM113.526 106.389L106.883 101.483L117.636 103.271L113.526 106.389ZM114.123 107.67L120.922 106.681L113.149 115.12L114.123 107.67ZM105.98 101.977L112.974 107.316L111.961 115.126L105.98 101.977Z' fill='%23FF060A'/%3E%3C/svg%3E%0A";

        return chainName switch
        {
            _ when chainName == ChainName.Mainnet => new TronUSDtLikeConfigurationItem
            {
                Currency = Constants.USDtCurrency,
                CurrencyDisplayName = Constants.USDtCurrencyDisplayName,
                DisplayName = $"{Constants.USDtCurrencyDisplayName} on {Constants.TronChainName}",
                CryptoImagePath = logo,

                DefaultRateRules =
                [
                    $"{Constants.USDtCurrency}_USD = 1",
                    $"{Constants.USDtCurrency}_BTC = USD_BTC",
                    $"{Constants.USDtCurrency}_X = {Constants.USDtCurrency}_BTC * BTC_X"
                ],

                Divisibility = 6,
                SmartContractAddress = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",
                JsonRpcUri = new Uri("https://api.trongrid.io/jsonrpc"),
                BlockExplorerLink = "https://tronscan.org/#/transaction/{0}"
            },
            _ when chainName == ChainName.Testnet => new TronUSDtLikeConfigurationItem
            {
                Currency = Constants.USDtCurrency,
                CurrencyDisplayName = Constants.USDtCurrencyDisplayName,
                DisplayName = $"{Constants.USDtCurrencyDisplayName} on {Constants.TronChainName} Testnet",
                CryptoImagePath = logo,

                DefaultRateRules =
                [
                    $"{Constants.USDtCurrency}_USD = 1",
                    $"{Constants.USDtCurrency}_X = {Constants.USDtCurrency}_BTC * BTC_X"
                ],

                Divisibility = 6,
                // On testnet, we use a different smart contract address
                // TXLAQ63Xg1NAzckPwKHvzw7CSEmLMEqcdj seems not the official one (TXYZopYRdj2D9XRtbG411XZZ3kM5VkAeBf), but at least
                // there is a faucet for it: https://nileex.io/join/getJoinPage
                SmartContractAddress = "TXLAQ63Xg1NAzckPwKHvzw7CSEmLMEqcdj",
                JsonRpcUri = new Uri("https://nile.trongrid.io/jsonrpc"),
                BlockExplorerLink = "https://nile.tronscan.org/#/transaction/{0}"
            },

            _ => throw new NotSupportedException()
        };
    }

    public static TronUSDtLikeConfigurationItem GetTronUSDtLikeDefaultConfigurationItem(NBXplorerNetworkProvider networkProvider, IConfiguration configuration)
    {
        var tronUSDtConfiguration = GetTronUSDtHardcodedConfig(networkProvider.NetworkType);
        tronUSDtConfiguration = OverrideWithAppConfig(tronUSDtConfiguration, configuration);
        return tronUSDtConfiguration;
    }

    private static TronUSDtLikeConfigurationItem OverrideWithAppConfig(TronUSDtLikeConfigurationItem config, IConfiguration configuration)
    {
        return config with
        {
            JsonRpcUri = configuration.GetOrDefault($"{config.GetSettingPrefix()}_JSONRPC_URI", config.JsonRpcUri),
            SmartContractAddress = configuration.GetOrDefault($"{config.GetSettingPrefix()}_SMARTCONTRACT_ADDRESS", config.SmartContractAddress)
        };
    }

    public static TronUSDtLikeConfigurationItem OverrideWithServerSettings(TronUSDtLikeConfigurationItem config, ISettingsRepository settingsRepository)
    {
        var serverSettings = settingsRepository.GetSettingAsync<TronUSDtLikeServerSettings>(ServerSettingsKey(config)).Result;

        if (serverSettings == null)
            return config;

        return config with
        {
            JsonRpcUri = serverSettings.JsonRpcUri ?? config.JsonRpcUri,
            SmartContractAddress = (serverSettings.SmartContractAddress ?? config.SmartContractAddress).ToLowerInvariant()
        };
    }

    // Ethereum USDT like configuration helpers
    public static EthUSDtLikeConfigurationItem GetEthUSDtLikeDefaultConfigurationItem(NBXplorerNetworkProvider networkProvider, IConfiguration configuration)
    {
        var ethConfig = GetEthUSDtHardcodedConfig(networkProvider.NetworkType);
        ethConfig = OverrideWithAppConfig(ethConfig, configuration);
        return ethConfig;
    }

    private static EthUSDtLikeConfigurationItem OverrideWithAppConfig(EthUSDtLikeConfigurationItem config, IConfiguration configuration)
    {
        return config with
        {
            JsonRpcUri = configuration.GetOrDefault($"{config.GetSettingPrefix()}_JSONRPC_URI", config.JsonRpcUri),
            SmartContractAddress = configuration.GetOrDefault($"{config.GetSettingPrefix()}_SMARTCONTRACT_ADDRESS", config.SmartContractAddress).ToLowerInvariant()
        };
    }

    public static EthUSDtLikeConfigurationItem OverrideWithServerSettings(EthUSDtLikeConfigurationItem config, ISettingsRepository settingsRepository)
    {
        var serverSettings = settingsRepository.GetSettingAsync<EthUSDtLikeServerSettings>(ServerSettingsKey(config)).Result;

        if (serverSettings == null)
            return config;

        return config with
        {
            JsonRpcUri = serverSettings.JsonRpcUri ?? config.JsonRpcUri,
            SmartContractAddress = (serverSettings.SmartContractAddress ?? config.SmartContractAddress).ToLowerInvariant()
        };
    }

    private static EthUSDtLikeConfigurationItem GetEthUSDtHardcodedConfig(ChainName chainName)
    {
        const string ethLogo =
            "data:image/svg+xml,%3Csvg width='165' height='165' viewBox='0 0 165 165' fill='none' xmlns='http://www.w3.org/2000/svg'%3E" +
            "%3Ccircle cx='82.5' cy='82.5' r='82.5' fill='%23019493'/%3E" +
            // Main Tether coin (same as TRON variant)
            "%3Cpath d='M128.225 75.7991L112.84 49.382C112.209 48.3018 111.049 47.6387 109.787 47.6387H56.9688C55.7442 47.6387 54.6051 48.2697 53.9634 49.2964L37.5196 75.7724C36.664 77.152 36.8832 78.9274 38.0436 80.0665L80.3431 121.355C81.7121 122.697 83.9206 122.697 85.295 121.355L127.648 80.013C128.787 78.9007 129.022 77.1734 128.225 75.8044V75.7991ZM106.477 83.1306C106.418 85.8365 98.4875 88.0879 87.9206 88.6387V101.254H77.9313V88.6387C67.3645 88.0879 59.4394 85.8365 59.3805 83.1306V77.4943C59.4394 74.7884 67.3645 72.5317 77.9313 71.9809V66.8419H62.8886V59.4301H102.969V66.8419H87.926V71.9809C98.4928 72.5317 106.423 74.7884 106.482 77.4943V83.1306H106.477Z' fill='white'/%3E" +
            "%3Cpath d='M87.9206 81.3659C86.311 81.4515 84.6372 81.4889 82.926 81.4889C81.2148 81.4889 79.541 81.4462 77.9313 81.3659V75.9809C68.9527 76.4462 61.8725 78.1467 59.9206 80.3071C62.2255 82.8526 71.6533 84.7617 82.9313 84.7617C94.2094 84.7617 103.632 82.8526 105.937 80.3071C103.979 78.1467 96.91 76.4462 87.926 75.9809V81.3659H87.9206Z' fill='white'/%3E" +
            // Small badge circle bottom-right
            "%3Ccircle cx='113' cy='108.5' r='19' fill='%23019493'/%3E" +
            "%3Ccircle cx='113' cy='108.5' r='17' fill='white'/%3E" +
            // Ethereum diamond inside the small circle (centered and scaled to fit)
            "%3Cg transform='translate(103,92) scale(0.5)'%3E" +
            "%3Cpolygon fill='%23343434' points='20,0 0,34 20,26 40,34'/%3E" +
            "%3Cpolygon fill='%238C8C8C' points='20,26 0,34 20,44 40,34'/%3E" +
            "%3Cpolygon fill='%233C3C3D' points='20,54 0,40 20,66 40,40'/%3E" +
            "%3Cpolygon fill='%23141414' points='20,44 0,40 20,54 40,40'/%3E" +
            "%3C/g%3E" +
            "%3C/svg%3E%0A";

        return chainName switch
        {
            _ when chainName == ChainName.Mainnet => new EthUSDtLikeConfigurationItem(Constants.EthereumChainName)
            {
                Currency = Constants.USDtCurrency,
                CurrencyDisplayName = Constants.USDtCurrencyDisplayName,
                DisplayName = $"{Constants.USDtCurrencyDisplayName} on {Constants.EthereumChainName}",
                CryptoImagePath = ethLogo,
                
                DefaultRateRules =
                [
                    $"{Constants.USDtCurrency}_USD = 1",
                    $"{Constants.USDtCurrency}_BTC = USD_BTC",
                    $"{Constants.USDtCurrency}_X = {Constants.USDtCurrency}_BTC * BTC_X"
                ],

                Divisibility = 6,
                SmartContractAddress = "0xdac17f958d2ee523a2206206994597c13d831ec7",
                JsonRpcUri = new Uri("https://ethereum.publicnode.com"),
                BlockExplorerLink = "https://etherscan.io/tx/{0}"
            },
            _ when chainName == ChainName.Testnet => new EthUSDtLikeConfigurationItem(Constants.SepoliaChainName)
            {
                Currency = Constants.USDtCurrency,
                CurrencyDisplayName = Constants.USDtCurrencyDisplayName,
                DisplayName = $"{Constants.USDtCurrencyDisplayName} on {Constants.SepoliaChainName}",
                CryptoImagePath = ethLogo,

                DefaultRateRules =
                [
                    $"{Constants.USDtCurrency}_USD = 1",
                    $"{Constants.USDtCurrency}_X = {Constants.USDtCurrency}_BTC * BTC_X"
                ],

                Divisibility = 6,
                // Token contract should be overridden in server settings for sepolia.
                // This is one is for development only.
                // You can use mint() function on this contract to mint yourself some USDT test tokens.
                SmartContractAddress = "0xf02d1AF3c9Ec13f4E9E986f2de75dA96D75a57B0",
                // Provide a sensible default public RPC; can be overridden in settings
                JsonRpcUri = new Uri("https://sepolia.publicnode.com"),
                BlockExplorerLink = "https://sepolia.etherscan.io/tx/{0}"
            },
            _ => throw new NotSupportedException()
        };
    }

    public static string? ServerSettingsKey(USDtPluginConfigurationItem item)
    {
        return $"{item.GetSettingPrefix()}_SERVER_SETTINGS";
    }
}