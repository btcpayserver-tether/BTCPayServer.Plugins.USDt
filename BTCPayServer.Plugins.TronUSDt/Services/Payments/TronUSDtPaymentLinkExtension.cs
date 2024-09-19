using System;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.TronUSDt.Services.Payments;

public class TronUSDtPaymentLinkExtension(PaymentMethodId paymentMethodId, TronUSDtLikeSpecificBtcPayNetwork network) : IPaymentLinkExtension
{
    private readonly TronUSDtLikeSpecificBtcPayNetwork _network = network;

    public PaymentMethodId PaymentMethodId { get; } = paymentMethodId;

    public string? GetPaymentLink(PaymentPrompt prompt, IUrlHelper? urlHelper)
    {
        throw new NotImplementedException();
        // var due = prompt.Calculate().Due;
        // return $"{_network.UriScheme}:{prompt.Destination}?tx_amount={due.ToString(CultureInfo.InvariantCulture)}";
    }
}