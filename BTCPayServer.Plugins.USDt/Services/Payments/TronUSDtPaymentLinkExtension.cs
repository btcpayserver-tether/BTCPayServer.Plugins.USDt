using System.Globalization;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class TronUSDtPaymentLinkExtension(PaymentMethodId paymentMethodId, USDtPluginConfiguration pluginConfiguration)
    : IPaymentLinkExtension
{
    public PaymentMethodId PaymentMethodId { get; } = paymentMethodId;

    public string? GetPaymentLink(PaymentPrompt prompt, IUrlHelper? urlHelper)
    {
        var configuration = pluginConfiguration.TronUSDtLikeConfigurationItems[paymentMethodId];
        var template = prompt.Details?.Value<string?>("paymentLinkTemplate");
        var format = USDtPaymentLinkFormats.ResolveTron(
            USDtPaymentLinkFormats.ReadFormat(prompt.Details, false),
            template,
            prompt.Details?.Value<bool?>("excludeAmountFromPaymentLink") ?? false);

        return BuildPaymentLink(
            prompt.Destination,
            prompt.Calculate().Due,
            format,
            template,
            configuration.SmartContractAddress,
            configuration.Divisibility);
    }

    internal static string? BuildPaymentLink(string? destination, decimal due, bool excludeAmount)
    {
        return BuildPaymentLink(
            destination,
            due,
            excludeAmount ? USDtPaymentLinkFormat.StandardWithoutAmount : USDtPaymentLinkFormat.Standard,
            null,
            string.Empty,
            0);
    }

    internal static string? BuildPaymentLink(
        string? destination,
        decimal due,
        USDtPaymentLinkFormat format,
        string? template,
        string smartContractAddress,
        int divisibility)
    {
        if (string.IsNullOrEmpty(destination))
            return null;

        switch (format)
        {
            case USDtPaymentLinkFormat.StandardWithoutAmount:
                return $"tron:{destination}";
            case USDtPaymentLinkFormat.AddressOnly:
                return destination;
            case USDtPaymentLinkFormat.Custom:
                var values = USDtPaymentLinkFormats.CreateTemplateValues(
                    destination,
                    due,
                    divisibility,
                    smartContractAddress);
                if (USDtPaymentLinkFormats.TryRenderTemplate(template, false, values, out var rendered, out _))
                    return rendered;
                break;
        }

        return $"tron:{destination}?amount={due.ToString(CultureInfo.InvariantCulture)}";
    }
}
