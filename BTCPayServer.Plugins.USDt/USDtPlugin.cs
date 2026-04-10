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
using BTCPayServer.Plugins.USDt.Configuration.EVM;
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

        var tronUSDtConfiguration = USDtConfigurationProvider.GetTronUSDtLikeDefaultConfigurationItem(networkProvider, configuration);
        var evmUsdtConfigurations = USDtConfigurationProvider.GetEVMUSDtLikeDefaultConfigurationItems(networkProvider, configuration);

        var pluginConfiguration = new USDtPluginConfiguration
        {
            TronUSDtLikeConfigurationItems = new Dictionary<PaymentMethodId, TronUSDtLikeConfigurationItem>
            {
                { tronUSDtConfiguration.GetPaymentMethodId(), tronUSDtConfiguration }
            },
            EVMUSDtLikeConfigurationItems = evmUsdtConfigurations
        };

        services.AddSingleton(pluginConfiguration);
        services.AddHostedService<USDtPluginConfigurationBootstrapper>();

        services.AddCurrencyData(new CurrencyData
        {
            Code = Constants.USDtCurrency,
            Name = Constants.USDtCurrencyDisplayName,
            Divisibility = 6,
            Symbol = Constants.USDtCurrencyDisplayName,
            Crypto = true
        });

        var tronUSDtPaymentMethodId = tronUSDtConfiguration.GetPaymentMethodId();
        services.AddSingleton<TronUSDtRPCProvider>();
        services.AddHostedService<TronUSDtLikeSummaryUpdaterHostedService>();
        services.AddHostedService<TronUSDtListener>();

        services.AddSingleton(new DefaultRules(tronUSDtConfiguration.DefaultRateRules));

        RegisterTronPaymentMethodServices(services, tronUSDtConfiguration);

        // For future usages (multiple TRC20)
        //services.AddSingleton<IUIExtension>(new UIExtension("TronUSDt/StoreNavTronUSDtExtension", "store-integrations-nav"));
        services.AddUIExtension("server-nav","TronUSDtLike/ServerNavTronUSDtExtension");
        services.AddUIExtension("store-wallets-nav", "TronUSDtLike/StoreWalletsNavTronUSDtExtension");
        services.AddUIExtension("store-invoices-payments", "TronUSDtLike/ViewTronUSDtLikePaymentData");
        services.AddUIExtension("checkout-payment-method", "EmptyCheckoutPaymentMethodExtension");
        services.AddSingleton<ISyncSummaryProvider, TronUSDtSyncSummaryProvider>();
        
        services.AddSingleton<EVMUSDtRPCProvider>();
        services.AddHostedService<EVMUSDtLikeSummaryUpdaterHostedService>();
        services.AddHostedService<EVMUSDtListener>();

        foreach (var evmUsdtConfiguration in evmUsdtConfigurations.Values)
        {
            RegisterEvmPaymentMethodServices(services, evmUsdtConfiguration);
        }

        services.AddSingleton<ISyncSummaryProvider, EVMUSDtSyncSummaryProvider>();

        // Store UI extensions for all EVM chains (addresses management, checkout, server nav)
        services.AddUIExtension("store-wallets-nav", "EVMUSDtLike/StoreWalletsNavEVMUSDtExtension");
        services.AddUIExtension("checkout-payment-method", "EmptyCheckoutPaymentMethodExtension");
        services.AddUIExtension("server-nav", "EVMUSDtLike/ServerNavEVMUSDtExtension");

        services.AddSingleton<ISwaggerProvider, SwaggerProvider>();
    }

    private static void RegisterTronPaymentMethodServices(
        IServiceCollection services,
        TronUSDtLikeConfigurationItem configuration)
    {
        var paymentMethodId = configuration.GetPaymentMethodId();

        RegisterCommonPaymentMethodServices(services, paymentMethodId, configuration.BlockExplorerLink,
            configuration.CryptoImagePath, configuration.CurrencyDisplayName, configuration.DisplayName);

        services.AddSingleton(provider => (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider,
            typeof(TronUSDtLikePaymentMethodHandler), configuration));
        services.AddSingleton<IPaymentLinkExtension>(provider =>
            (IPaymentLinkExtension)ActivatorUtilities.CreateInstance(provider, typeof(TronUSDtPaymentLinkExtension),
                paymentMethodId));
    }

    private static void RegisterEvmPaymentMethodServices(
        IServiceCollection services,
        EVMUSDtLikeConfigurationItem configuration)
    {
        var paymentMethodId = configuration.GetPaymentMethodId();

        RegisterCommonPaymentMethodServices(services, paymentMethodId, configuration.BlockExplorerLink,
            configuration.CryptoImagePath, configuration.CurrencyDisplayName, configuration.DisplayName);

        services.AddSingleton(provider => (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider,
            typeof(EVMUSDtPaymentMethodHandler), configuration));
        services.AddSingleton<IPaymentLinkExtension>(provider =>
            (IPaymentLinkExtension)ActivatorUtilities.CreateInstance(provider, typeof(EVMUSDtPaymentLinkExtension),
                paymentMethodId));
    }

    private static void RegisterCommonPaymentMethodServices(
        IServiceCollection services,
        PaymentMethodId paymentMethodId,
        string blockExplorerLink,
        string cryptoImagePath,
        string currencyDisplayName,
        string displayName)
    {
        services.AddTransactionLinkProvider(paymentMethodId, new USDtTransactionLinkProvider(blockExplorerLink));
        services.AddSingleton(provider =>
            (ICheckoutModelExtension)ActivatorUtilities.CreateInstance(provider, typeof(USDtCheckoutModelExtension),
                paymentMethodId, cryptoImagePath, currencyDisplayName));
        services.AddDefaultPrettyName(paymentMethodId, displayName);
    }


}