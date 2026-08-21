using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration.EVM;
using BTCPayServer.Services.Rates;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class EVMUSDtPaymentMethodHandler(
    EVMUSDtLikeConfigurationItem configurationItem,
    EVMUSDtRPCProvider rpcProvider,
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
        if (!rpcProvider.IsAvailable(configurationItem.GetPaymentMethodId()))
            throw new PaymentMethodUnavailableException("Node or wallet not available");

        if (!configurationItem.HasValidSmartContractAddress())
            throw new PaymentMethodUnavailableException(
                $"{configurationItem.DisplayName} is not configured with a smart contract address yet");

        var config = ParsePaymentMethodConfig(context.PaymentMethodConfig);
        var details = CreatePaymentPromptDetails(config);
        var availableAddress = await config
                                   .GetOneNotReservedAddress(context.PaymentMethodId, trackedInvoiceProvider) ??
                               throw new PaymentMethodUnavailableException(
                                   $"All your {configurationItem.Chain} addresses are currently waiting payment");
        context.Prompt.Destination = availableAddress;
        context.Prompt.PaymentMethodFee = 0;
        context.Prompt.Details = JObject.FromObject(details, Serializer);
    }

    internal static EVMUSDtLikeOnChainPaymentMethodDetails CreatePaymentPromptDetails(EVMUSDtPaymentMethodConfig config)
    {
        var paymentLinkFormat = USDtPaymentLinkFormats.ResolveEvm(
            config.PaymentLinkFormat,
            config.PaymentLinkTemplate);
        return new EVMUSDtLikeOnChainPaymentMethodDetails
        {
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

    private EVMUSDtPaymentMethodConfig ParsePaymentMethodConfig(JToken config)
    {
        return config.ToObject<EVMUSDtPaymentMethodConfig>(Serializer) ??
               throw new FormatException($"Invalid {nameof(EVMUSDtPaymentMethodHandler)}");
    }

    public Task ValidatePaymentMethodConfig(PaymentMethodConfigValidationContext validationContext)
    {
        var config = ParsePaymentMethodConfig(validationContext.Config);
        var previousConfig = validationContext.PreviousConfig is null
            ? null
            : ParsePaymentMethodConfig(validationContext.PreviousConfig);
        var templateValues = USDtPaymentLinkFormats.CreateTemplateValues(
            "0x742d35cc6634c0532925a3b844bc454e4438f44e",
            12.34m,
            configurationItem.Divisibility,
            configurationItem.SmartContractAddress.ToLowerInvariant(),
            configurationItem.ChainId);
        if (config.PaymentLinkFormat is { } format)
        {
            var error = USDtPaymentLinkFormats.ValidateSelection(
                format,
                config.PaymentLinkTemplate,
                true,
                templateValues);
            if (error is not null)
                validationContext.ModelState.AddModelError(nameof(config.PaymentLinkFormat), error);
        }
        else if (!string.IsNullOrWhiteSpace(config.PaymentLinkTemplate))
        {
            var error = USDtPaymentLinkFormats.ValidateSelection(
                USDtPaymentLinkFormat.Custom,
                config.PaymentLinkTemplate,
                true,
                templateValues);
            if (error is not null)
                validationContext.ModelState.AddModelError(nameof(config.PaymentLinkTemplate), error);
        }
        config.PreserveActivationFrom(previousConfig);
        validationContext.Config = JToken.FromObject(config, Serializer);
        return Task.CompletedTask;
    }

    public EVMUSDtLikeOnChainPaymentMethodDetails? ParsePaymentPromptDetails(JToken details)
    {
        return details.ToObject<EVMUSDtLikeOnChainPaymentMethodDetails>(Serializer);
    }

    public EVMUSDtPaymentData ParsePaymentDetails(JToken details)
    {
        return details.ToObject<EVMUSDtPaymentData>(Serializer) ??
               throw new FormatException($"Invalid {nameof(EVMUSDtPaymentMethodHandler)}");
    }
}
