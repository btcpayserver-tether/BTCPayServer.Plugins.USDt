using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class TronUSDtPaymentModelExtension(
    PaymentMethodId paymentMethodId,
    IEnumerable<IPaymentLinkExtension> paymentLinkExtensions,
    BTCPayNetworkBase network) : IPaymentModelExtension
{
    private readonly IPaymentLinkExtension _paymentLinkExtension = paymentLinkExtensions.Single(p => p.PaymentMethodId == paymentMethodId);

    public PaymentMethodId PaymentMethodId { get; } = paymentMethodId;
    public string DisplayName => network.DisplayName;
    public string Image => network.CryptoImagePath;
    public string Badge => "";

    public void ModifyPaymentModel(PaymentModelContext context)
    {
        if (context.Model.Activated)
        {
            context.Model.InvoiceBitcoinUrl = _paymentLinkExtension.GetPaymentLink(context.Prompt, context.UrlHelper);
            context.Model.InvoiceBitcoinUrlQR = context.Model.InvoiceBitcoinUrl;
            context.Model.ShowPayInWalletButton = false;
        }
        else
        {
            context.Model.InvoiceBitcoinUrl = "";
            context.Model.InvoiceBitcoinUrlQR = "";
            context.Model.ShowPayInWalletButton = false;
        }
    }
}