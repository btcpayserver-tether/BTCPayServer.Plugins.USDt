using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration.Ethereum;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class EthUSDtPaymentMethodHandler(
    EthUSDtLikeConfigurationItem configurationItem,
    EthUSDtRPCProvider rpcProvider,
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
        if (!rpcProvider.IsAvailable(configurationItem.GetPaymentMethodId()))
            throw new PaymentMethodUnavailableException("Node or wallet not available");

        var details = new EthUSDtLikeOnChainPaymentMethodDetails();
        var availableAddress = await ParsePaymentMethodConfig(context.PaymentMethodConfig)
                                   .GetOneNotReservedAddress(context.PaymentMethodId, invoiceRepository) ??
                               throw new PaymentMethodUnavailableException(
                                   "All your Ethereum addresses are currently waiting payment");
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

    private EthUSDtPaymentMethodConfig ParsePaymentMethodConfig(JToken config)
    {
        return config.ToObject<EthUSDtPaymentMethodConfig>(Serializer) ??
               throw new FormatException($"Invalid {nameof(EthUSDtPaymentMethodHandler)}");
    }

    public EthUSDtLikeOnChainPaymentMethodDetails? ParsePaymentPromptDetails(JToken details)
    {
        return details.ToObject<EthUSDtLikeOnChainPaymentMethodDetails>(Serializer);
    }

    public EthUSDtPaymentData ParsePaymentDetails(JToken details)
    {
        return details.ToObject<EthUSDtPaymentData>(Serializer) ??
               throw new FormatException($"Invalid {nameof(EthUSDtPaymentMethodHandler)}");
    }
}