using BTCPayServer.Plugins.USDt.Services.Payments;

namespace BTCPayServer.Plugins.USDt.Controllers.ViewModels;

// Only bind submitted settings. Display names, balances and preview values belong
// to the GET view models and must not participate in POST validation.
public class EditUSDtPaymentMethodInputModel
{
    public string? Address { get; set; }
    public bool Enabled { get; set; }
    public USDtPaymentLinkFormat PaymentLinkFormat { get; set; } = USDtPaymentLinkFormat.Standard;
    public string? PaymentLinkTemplate { get; set; }
}
