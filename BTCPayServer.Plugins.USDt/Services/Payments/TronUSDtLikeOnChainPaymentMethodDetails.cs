namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class TronUSDtLikeOnChainPaymentMethodDetails
{
    public bool ExcludeAmountFromPaymentLink { get; set; }
    public USDtPaymentLinkFormat? PaymentLinkFormat { get; set; }
    public string? PaymentLinkTemplate { get; set; }
}
