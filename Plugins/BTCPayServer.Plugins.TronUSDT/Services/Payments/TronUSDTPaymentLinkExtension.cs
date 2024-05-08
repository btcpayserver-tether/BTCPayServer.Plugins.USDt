#nullable enable
using System;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.TronUSDT.Tron.TronUSDT;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.TronUSDT.Services.Payments;

public class TronUSDTPaymentLinkExtension : IPaymentLinkExtension
{
    private readonly TronUSDTLikeSpecificBtcPayNetwork _network;

    public TronUSDTPaymentLinkExtension(PaymentMethodId paymentMethodId, TronUSDTLikeSpecificBtcPayNetwork network)
    {
        PaymentMethodId = paymentMethodId;
        _network = network;
    }

    public PaymentMethodId PaymentMethodId { get; }

    public string? GetPaymentLink(PaymentPrompt prompt, IUrlHelper? urlHelper)
    {
        throw new NotImplementedException();
        // var due = prompt.Calculate().Due;
        // return $"{_network.UriScheme}:{prompt.Destination}?tx_amount={due.ToString(CultureInfo.InvariantCulture)}";
    }
}