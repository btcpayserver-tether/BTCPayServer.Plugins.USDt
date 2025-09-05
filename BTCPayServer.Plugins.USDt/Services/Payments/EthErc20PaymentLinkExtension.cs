using System.Globalization;
using System.Numerics;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class EthErc20PaymentLinkExtension(PaymentMethodId paymentMethodId, string tokenContract, int decimals) : IPaymentLinkExtension
{
    public PaymentMethodId PaymentMethodId { get; } = paymentMethodId;
    private readonly string _contract = tokenContract.ToLowerInvariant();
    private readonly int _decimals = decimals;

    public string? GetPaymentLink(PaymentPrompt prompt, IUrlHelper? urlHelper)
    {
        if (string.IsNullOrEmpty(prompt.Destination))
            return null;

        var to = prompt.Destination.ToLowerInvariant();
        var due = prompt.Calculate().Due;

        // Convert due (decimal) to base units using decimals, truncating (floor) to avoid overstating value
        // scale = 10^decimals using decimal math to preserve precision
        decimal scale = 1m;
        for (var i = 0; i < _decimals; i++) scale *= 10m;
        var scaled = due * scale;
        if (scaled < 0) scaled = 0; // no negative amounts
        var unitsDec = decimal.Truncate(scaled);
        var amountUnits = BigInteger.Parse(unitsDec.ToString("0", CultureInfo.InvariantCulture));

        // EIP-681 ERC-20 transfer link: ethereum:{contract}/transfer?address={to}&uint256={amount}
        return $"ethereum:{_contract}/transfer?address={to}&uint256={amountUnits}";
    }
}
