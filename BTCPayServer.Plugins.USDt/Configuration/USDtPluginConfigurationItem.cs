using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.USDt.Configuration;

public abstract record USDtPluginConfigurationItem
{
    public abstract string Chain { get;  }
    public abstract string Currency { get; init; }

    public ChainRef ChainRef => Chain;
    public CurrencyRef CurrencyRef => Currency;

    public PaymentMethodId GetPaymentMethodId() => new($"{CurrencyRef}-{ChainRef}");
    public string GetSettingPrefix() => $"{CurrencyRef}_{ChainRef}";
}