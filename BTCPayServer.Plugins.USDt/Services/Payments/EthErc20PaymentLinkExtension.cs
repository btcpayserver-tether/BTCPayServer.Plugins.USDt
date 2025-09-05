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

        // Convert due (decimal) to base units using decimals without floating point errors
        var formatted = due.ToString($"F{_decimals}", CultureInfo.InvariantCulture); // fixed number of decimals
        var digits = formatted.Replace(".", "");
        if (!BigInteger.TryParse(digits, out var amountUnits))
            return null;

        // EIP-681 ERC-20 transfer link: ethereum:{contract}/transfer?address={to}&uint256={amount}
        return $"ethereum:{_contract}/transfer?address={to}&uint256={amountUnits}";
    }
}
