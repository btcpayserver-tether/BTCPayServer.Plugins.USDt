using System.Globalization;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class TronUSDtPaymentLinkExtension(PaymentMethodId paymentMethodId) : IPaymentLinkExtension
{
    public PaymentMethodId PaymentMethodId { get; } = paymentMethodId;

    public string? GetPaymentLink(PaymentPrompt prompt, IUrlHelper? urlHelper)
    {
        var due = prompt.Calculate().Due;
        return $"{prompt.Destination}?amount={due.ToString(CultureInfo.InvariantCulture)}";
    }
}