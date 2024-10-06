using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.Tron;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class TronUSDtPaymentModelExtension(
    TronUSDtLikeConfigurationItem configurationItem,
    IEnumerable<IPaymentLinkExtension> paymentLinkExtensions) : IPaymentModelExtension
{
    private readonly IPaymentLinkExtension _paymentLinkExtension = paymentLinkExtensions.Single(p => p.PaymentMethodId == configurationItem.GetPaymentMethodId());

    public PaymentMethodId PaymentMethodId { get; } = configurationItem.GetPaymentMethodId();
    public string DisplayName => configurationItem.DisplayName;
    public string Image => configurationItem.CryptoImagePath;
    public string Badge => "";

    public void ModifyPaymentModel(PaymentModelContext context)
    {
        if (context.Model.Activated)
        {
            context.Model.InvoiceBitcoinUrl = _paymentLinkExtension.GetPaymentLink(context.Prompt, context.UrlHelper);
            context.Model.InvoiceBitcoinUrlQR = context.Model.InvoiceBitcoinUrl;
            context.Model.ShowPayInWalletButton = false;
            context.Model.CryptoCode = configurationItem.CurrencyDisplayName;
        }
        else
        {
            context.Model.InvoiceBitcoinUrl = "";
            context.Model.InvoiceBitcoinUrlQR = "";
            context.Model.ShowPayInWalletButton = false;
        }
    }
}