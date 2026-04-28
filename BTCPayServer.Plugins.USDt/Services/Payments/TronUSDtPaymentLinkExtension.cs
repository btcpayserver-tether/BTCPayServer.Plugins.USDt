using System.Globalization;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class TronUSDtPaymentLinkExtension(PaymentMethodId paymentMethodId) : IPaymentLinkExtension
{
    public PaymentMethodId PaymentMethodId { get; } = paymentMethodId;

    public string? GetPaymentLink(PaymentPrompt prompt, IUrlHelper? urlHelper)
    {
        return BuildPaymentLink(
            prompt.Destination,
            prompt.Calculate().Due,
            prompt.Details?.Value<bool?>("excludeAmountFromPaymentLink") ?? false);
    }

    internal static string? BuildPaymentLink(string? destination, decimal due, bool excludeAmount)
    {
        if (string.IsNullOrEmpty(destination))
            return null;

        if (excludeAmount)
            return destination;

        return $"{destination}?amount={due.ToString(CultureInfo.InvariantCulture)}";
    }
}