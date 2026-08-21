namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class EVMUSDtLikeOnChainPaymentMethodDetails
{
    public USDtPaymentLinkFormat? PaymentLinkFormat { get; set; }
    public string? PaymentLinkTemplate { get; set; }
}
