using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration.Tron;
using BTCPayServer.Services.Rates;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class TronUSDtLikePaymentMethodHandler(
    TronUSDtLikeConfigurationItem configurationItem,
    TronUSDtRPCProvider tronUSDtRpcProvider,
    CurrencyNameTable currencyNameTable,
    USDtTrackedInvoiceProvider trackedInvoiceProvider) : IPaymentMethodHandler
{
    public JsonSerializer Serializer { get; } = BlobSerializer.CreateSerializer().Serializer;

    public PaymentMethodId PaymentMethodId { get; } = configurationItem.GetPaymentMethodId();

    public Task BeforeFetchingRates(PaymentMethodContext context)
    {
        context.Prompt.Currency = configurationItem.Currency;
        context.Prompt.Divisibility = configurationItem.Divisibility;
        context.Prompt.RateDivisibility = null;
        return Task.CompletedTask;
    }

    public async Task ConfigurePrompt(PaymentMethodContext context)
    {
        if (!tronUSDtRpcProvider.IsAvailable(configurationItem.GetPaymentMethodId()))
            throw new PaymentMethodUnavailableException("Node or wallet not available");

        var config = ParsePaymentMethodConfig(context.PaymentMethodConfig);
        var details = CreatePaymentPromptDetails(config);
        var availableAddress = await config
                                   .GetOneNotReservedAddress(context.PaymentMethodId, trackedInvoiceProvider) ??
                               throw new PaymentMethodUnavailableException(
                                   "All your TRON addresses are currently waiting payment");
        context.Prompt.Destination = availableAddress;
        context.Prompt.PaymentMethodFee = 0;
        context.Prompt.Details = JObject.FromObject(details, Serializer);
    }

    internal static TronUSDtLikeOnChainPaymentMethodDetails CreatePaymentPromptDetails(
        TronUSDtPaymentMethodConfig config)
    {
        var paymentLinkFormat = USDtPaymentLinkFormats.ResolveTron(
            config.PaymentLinkFormat,
            config.PaymentLinkTemplate,
            config.ExcludeAmountFromPaymentLink);
        return new TronUSDtLikeOnChainPaymentMethodDetails
        {
            ExcludeAmountFromPaymentLink = USDtPaymentLinkFormats.LegacyExcludeAmount(
                paymentLinkFormat,
                config.PaymentLinkTemplate),
            PaymentLinkFormat = paymentLinkFormat,
            PaymentLinkTemplate = paymentLinkFormat == USDtPaymentLinkFormat.Custom
                ? config.PaymentLinkTemplate
                : null
        };
    }

    object IPaymentMethodHandler.ParsePaymentMethodConfig(JToken config)
    {
        return ParsePaymentMethodConfig(config);
    }

    object IPaymentMethodHandler.ParsePaymentPromptDetails(JToken details)
    {
        return ParsePaymentPromptDetails(details)!;
    }

    object IPaymentMethodHandler.ParsePaymentDetails(JToken details)
    {
        return ParsePaymentDetails(details);
    }

    private TronUSDtPaymentMethodConfig ParsePaymentMethodConfig(JToken config)
    {
        return config.ToObject<TronUSDtPaymentMethodConfig>(Serializer) ??
               throw new FormatException($"Invalid {nameof(TronUSDtLikePaymentMethodHandler)}");
    }

    public Task ValidatePaymentMethodConfig(PaymentMethodConfigValidationContext validationContext)
    {
        var config = ParsePaymentMethodConfig(validationContext.Config);
        var previousConfig = validationContext.PreviousConfig is null
            ? null
            : ParsePaymentMethodConfig(validationContext.PreviousConfig);
        var templateValues = USDtPaymentLinkFormats.CreateTemplateValues(
            "TNPeeaaFB7K9cmo4uQpcU32zGK8G1NYqeL",
            12.34m,
            configurationItem.Divisibility,
            configurationItem.SmartContractAddress);
        if (config.PaymentLinkFormat is { } format)
        {
            var error = USDtPaymentLinkFormats.ValidateSelection(
                format,
                config.PaymentLinkTemplate,
                false,
                templateValues);
            if (error is not null)
                validationContext.ModelState.AddModelError(nameof(config.PaymentLinkFormat), error);
        }
        else if (!string.IsNullOrWhiteSpace(config.PaymentLinkTemplate))
        {
            var error = USDtPaymentLinkFormats.ValidateSelection(
                USDtPaymentLinkFormat.Custom,
                config.PaymentLinkTemplate,
                false,
                templateValues);
            if (error is not null)
                validationContext.ModelState.AddModelError(nameof(config.PaymentLinkTemplate), error);
        }
        config.PreserveActivationFrom(previousConfig);
        validationContext.Config = JToken.FromObject(config, Serializer);
        return Task.CompletedTask;
    }

    public TronUSDtLikeOnChainPaymentMethodDetails? ParsePaymentPromptDetails(JToken details)
    {
        return details.ToObject<TronUSDtLikeOnChainPaymentMethodDetails>(Serializer);
    }

    public TronUSDtLikePaymentData ParsePaymentDetails(JToken details)
    {
        return details.ToObject<TronUSDtLikePaymentData>(Serializer) ??
               throw new FormatException($"Invalid {nameof(TronUSDtLikePaymentMethodHandler)}");
    }
}
