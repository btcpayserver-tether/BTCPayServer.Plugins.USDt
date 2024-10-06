using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class TronUSDtLikePaymentMethodHandler(
    TronUSDtLikeSpecificBtcPayNetwork network,
    TronUSDtRPCProvider tronUSDtRpcProvider,
    InvoiceRepository invoiceRepository,
    StoreRepository storeRepository) : IPaymentMethodHandler
{
    private readonly InvoiceRepository _invoiceRepository = invoiceRepository;
    private readonly StoreRepository _storeRepository = storeRepository;
    private readonly TronUSDtRPCProvider _tronUSDtRpcProvider = tronUSDtRpcProvider;

    internal static PaymentType TronUSDtLike => TronUSDtLikePaymentType.Instance;
    public TronUSDtLikeSpecificBtcPayNetwork Network { get; } = network;

    public JsonSerializer Serializer { get; } = BlobSerializer.CreateSerializer().Serializer;

    public PaymentMethodId PaymentMethodId { get; } = TronUSDtLike.GetPaymentMethodId(network.CryptoCode);

    public Task BeforeFetchingRates(PaymentMethodContext context)
    {
        context.Prompt.Currency = Network.CryptoCode;
        context.Prompt.Divisibility = Network.Divisibility;
        return Task.CompletedTask;
    }

    public async Task ConfigurePrompt(PaymentMethodContext context)
    {
        if (!_tronUSDtRpcProvider.IsAvailable(Network.CryptoCode))
            throw new PaymentMethodUnavailableException("Node or wallet not available");

        var details = new TronUSDtLikeOnChainPaymentMethodDetails();
        var availableAddress = await ParsePaymentMethodConfig(context.PaymentMethodConfig)
                                   .GetOneNotReservedAddress(context.PaymentMethodId, _invoiceRepository) ??
                               throw new PaymentMethodUnavailableException("All your TRON addresses are currently waiting payment");
        context.Prompt.Destination = availableAddress;
        context.Prompt.PaymentMethodFee = 0; //TODO: vbn
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