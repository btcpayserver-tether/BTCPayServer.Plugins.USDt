using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.Tron;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class TronUSDtLikePaymentMethodHandler(
    TronUSDtLikeConfigurationItem configurationItem,
    TronUSDtRPCProvider tronUSDtRpcProvider,
    CurrencyNameTable currencyNameTable,
    InvoiceRepository invoiceRepository) : IPaymentMethodHandler
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
        var details = new TronUSDtLikeOnChainPaymentMethodDetails
        {
            ExcludeAmountFromPaymentLink = config.ExcludeAmountFromPaymentLink
        };
        var availableAddress = await config
                                   .GetOneNotReservedAddress(context.PaymentMethodId, invoiceRepository) ??
                               throw new PaymentMethodUnavailableException("All your TRON addresses are currently waiting payment");
        context.Prompt.Destination = availableAddress;
        context.Prompt.PaymentMethodFee = 0; 
        context.Prompt.Details = JObject.FromObject(details, Serializer);
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