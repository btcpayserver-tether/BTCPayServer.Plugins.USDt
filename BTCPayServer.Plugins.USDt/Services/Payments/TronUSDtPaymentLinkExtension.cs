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
        var excludeAmount = prompt.Details?.Value<bool?>("ExcludeAmountFromPaymentLink") ?? false;
        
        if (excludeAmount)
        {
            return prompt.Destination;
        }
        
        var due = prompt.Calculate().Due;
        return $"{prompt.Destination}?amount={due.ToString(CultureInfo.InvariantCulture)}";
    }
}