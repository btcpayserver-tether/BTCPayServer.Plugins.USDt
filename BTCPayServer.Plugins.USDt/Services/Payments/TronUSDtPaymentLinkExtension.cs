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

        // Bare address, no URI scheme: the store setting documents this mode as "the QR
        // code will only contain the destination address", and QR scanners that reject
        // tron: URIs (e.g. TronLink's in-Send scanner) only accept a plain base58 address.
        if (excludeAmount)
            return destination;

        return $"tron:{destination}?amount={due.ToString(CultureInfo.InvariantCulture)}";
    }
}
