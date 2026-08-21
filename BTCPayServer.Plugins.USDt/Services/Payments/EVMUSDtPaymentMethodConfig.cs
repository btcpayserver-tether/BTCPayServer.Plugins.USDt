namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class EVMUSDtPaymentMethodConfig : USDtPaymentMethodConfig
{
    public USDtPaymentLinkFormat? PaymentLinkFormat { get; set; }
    public string? PaymentLinkTemplate { get; set; }
}
