using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.TronUSDT.Tron.TronUSDT;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.TronUSDT.Services.Payments;

public class TronUSDTLikePaymentMethodHandler(
    TronUSDTLikeSpecificBtcPayNetwork network,
    TronUSDTRPCProvider tronUSDTRpcProvider,
    InvoiceRepository invoiceRepository,
    StoreRepository storeRepository) : IPaymentMethodHandler
{
    private readonly InvoiceRepository _invoiceRepository = invoiceRepository;
    private readonly StoreRepository _storeRepository = storeRepository;
    private readonly TronUSDTRPCProvider _tronUSDTRpcProvider = tronUSDTRpcProvider;

    internal static PaymentType TronUSDTLike => TronUSDTPaymentType.Instance;
    public TronUSDTLikeSpecificBtcPayNetwork Network { get; } = network;

    public JsonSerializer Serializer { get; } = BlobSerializer.CreateSerializer().Serializer;

    public PaymentMethodId PaymentMethodId { get; } = TronUSDTLike.GetPaymentMethodId(network.CryptoCode);

    public Task BeforeFetchingRates(PaymentMethodContext context)
    {
        context.Prompt.Currency = Network.CryptoCode;
        context.Prompt.Divisibility = Network.Divisibility;
        return Task.CompletedTask;
    }

    public async Task ConfigurePrompt(PaymentMethodContext context)
    {
        if (!_tronUSDTRpcProvider.IsAvailable(Network.CryptoCode))
            throw new PaymentMethodUnavailableException("Node or wallet not available");

        var details = new TronUSDTLikeOnChainPaymentMethodDetails();
        var availableAddress = await ParsePaymentMethodConfig(context.PaymentMethodConfig)
            .GetOneNotReservedAddress(context.PaymentMethodId, _invoiceRepository) ?? throw new PaymentMethodUnavailableException("All your TRON adresses are currently waiting payment");
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

    private TronUSDTPaymentMethodConfig ParsePaymentMethodConfig(JToken config)
    {
        return config.ToObject<TronUSDTPaymentMethodConfig>(Serializer) ??
               throw new FormatException($"Invalid {nameof(TronUSDTLikePaymentMethodHandler)}");
    }

    public TronUSDTLikeOnChainPaymentMethodDetails? ParsePaymentPromptDetails(JToken details)
    {
        return details.ToObject<TronUSDTLikeOnChainPaymentMethodDetails>(Serializer);
    }

    public TronUSDTLikePaymentData ParsePaymentDetails(JToken details)
    {
        return details.ToObject<TronUSDTLikePaymentData>(Serializer) ??
               throw new FormatException($"Invalid {nameof(TronUSDTLikePaymentMethodHandler)}");
    }
}