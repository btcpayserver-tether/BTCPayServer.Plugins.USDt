using System;
using System.Globalization;
using System.Numerics;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.EVM;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class EVMUSDtPaymentLinkExtension(PaymentMethodId paymentMethodId, USDtPluginConfiguration pluginConfiguration)
    : IPaymentLinkExtension
{
    public PaymentMethodId PaymentMethodId { get; } = paymentMethodId;

    public string? GetPaymentLink(PaymentPrompt prompt, IUrlHelper? urlHelper)
    {
        var configuration = pluginConfiguration.EVMUSDtLikeConfigurationItems[paymentMethodId];
        return BuildPaymentLink(
            prompt.Destination,
            configuration.SmartContractAddress,
            configuration.ChainId,
            configuration.Divisibility,
            prompt.Calculate().Due,
            prompt.Details?.Value<string?>("paymentLinkTemplate") ?? "ethereum:{smartContractAddress}@{chainId}/transfer?address={to}&uint256={amountUnits}");
    }

    internal static string? BuildPaymentLink(
        string? destination,
        string smartContractAddress,
        int chainId,
        int divisibility,
        decimal due,
        string paymentLinkTemplate)
    {
        if (string.IsNullOrEmpty(destination) ||
            string.IsNullOrWhiteSpace(smartContractAddress) ||
            string.Equals(smartContractAddress, EVMUSDtLikeConfigurationItem.UnconfiguredSmartContractAddress,
                StringComparison.OrdinalIgnoreCase))
            return null;

        var to = destination.ToLowerInvariant();

        // Convert due (decimal) to base units using decimals, truncating (floor) to avoid overstating value
        // scale = 10^decimals using decimal math to preserve precision
        var scale = 1m;
        for (var i = 0; i < divisibility; i++) scale *= 10m;
        var scaled = due * scale;
        if (scaled < 0) scaled = 0; // no negative amounts
        var unitsDec = decimal.Truncate(scaled);
        var amountUnits = BigInteger.Parse(unitsDec.ToString("0", CultureInfo.InvariantCulture));

        // EIP-681 ERC-20 transfer link: ethereum:{contract}@{chainId}/transfer?address={to}&uint256={amount}
        return paymentLinkTemplate
            .Replace("{smartContractAddress}", smartContractAddress.ToLowerInvariant())
            .Replace("{chainId}", chainId.ToString())
            .Replace("{to}", to)
            .Replace("{amountUnits}", amountUnits.ToString())
            .Replace("{amount}", due.ToString(CultureInfo.InvariantCulture));
    }
}