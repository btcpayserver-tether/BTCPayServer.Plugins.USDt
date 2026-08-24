using System;
using System.Globalization;
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
        var template = prompt.Details?.Value<string?>("paymentLinkTemplate");
        var format = USDtPaymentLinkFormats.ResolveEvm(
            USDtPaymentLinkFormats.ReadFormat(prompt.Details, true),
            template);
        return BuildPaymentLink(
            prompt.Destination,
            configuration.SmartContractAddress,
            configuration.ChainId,
            configuration.Divisibility,
            prompt.Calculate().Due,
            format,
            template);
    }

    internal static string? BuildPaymentLink(
        string? destination,
        string smartContractAddress,
        int chainId,
        int divisibility,
        decimal due)
    {
        return BuildPaymentLink(
            destination,
            smartContractAddress,
            chainId,
            divisibility,
            due,
            USDtPaymentLinkFormat.Standard,
            null);
    }

    internal static string? BuildPaymentLink(
        string? destination,
        string smartContractAddress,
        int chainId,
        int divisibility,
        decimal due,
        USDtPaymentLinkFormat format,
        string? template)
    {
        if (string.IsNullOrEmpty(destination) ||
            string.IsNullOrWhiteSpace(smartContractAddress) ||
            string.Equals(smartContractAddress, EVMUSDtLikeConfigurationItem.UnconfiguredSmartContractAddress,
                StringComparison.OrdinalIgnoreCase))
            return null;

        var to = destination.ToLowerInvariant();
        var contract = smartContractAddress.ToLowerInvariant();

        if (format == USDtPaymentLinkFormat.AddressOnly)
            return to;

        if (format == USDtPaymentLinkFormat.Custom)
        {
            var values = USDtPaymentLinkFormats.CreateTemplateValues(
                to,
                due,
                divisibility,
                contract,
                chainId);
            if (USDtPaymentLinkFormats.TryRenderTemplate(template, true, values, out var rendered, out _))
                return rendered;
        }

        var amountUnits = USDtPaymentLinkFormats.ToBaseUnits(due, divisibility);

        // EIP-681 ERC-20 transfer link: ethereum:{contract}@{chainId}/transfer?address={to}&uint256={amount}
        return $"ethereum:{contract}@{chainId.ToString(CultureInfo.InvariantCulture)}/transfer?address={to}&uint256={amountUnits.ToString(CultureInfo.InvariantCulture)}";
    }
}
