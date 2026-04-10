using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class USDtCheckoutModelExtension(
    PaymentMethodId paymentMethodId,
    string image,
    string currencyDisplayName,
    IEnumerable<IPaymentLinkExtension> paymentLinkExtensions) : ICheckoutModelExtension
{
    private readonly IPaymentLinkExtension _paymentLinkExtension =
        paymentLinkExtensions.Single(p => p.PaymentMethodId == paymentMethodId);

    public PaymentMethodId PaymentMethodId { get; } = paymentMethodId;
    public string Image => image;
    public string Badge => "";

    public void ModifyCheckoutModel(CheckoutModelContext context)
    {
        if (context.Handler?.PaymentMethodId != PaymentMethodId)
            return;

        context.Model.CheckoutBodyComponentName = BitcoinCheckoutModelExtension.CheckoutBodyComponentName;
        context.Model.InvoiceBitcoinUrl = _paymentLinkExtension.GetPaymentLink(context.Prompt, context.UrlHelper);
        context.Model.InvoiceBitcoinUrlQR = context.Model.InvoiceBitcoinUrl;
        context.Model.ShowPayInWalletButton = false;
        context.Model.PaymentMethodCurrency = currencyDisplayName;
    }
}