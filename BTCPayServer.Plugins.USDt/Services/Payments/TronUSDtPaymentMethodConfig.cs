namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class TronUSDtPaymentMethodConfig : USDtPaymentMethodConfig
{
    public bool ExcludeAmountFromPaymentLink { get; set; }
    public USDtPaymentLinkFormat? PaymentLinkFormat { get; set; }
    public string? PaymentLinkTemplate { get; set; }
}
