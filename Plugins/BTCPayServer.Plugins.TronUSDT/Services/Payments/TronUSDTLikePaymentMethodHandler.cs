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

public class TronUSDTLikePaymentMethodHandler : IPaymentMethodHandler
{
    private readonly InvoiceRepository _invoiceRepository;
    private readonly StoreRepository _storeRepository;
    private readonly TronUSDTRPCProvider _tronUSDTRpcProvider;

    public TronUSDTLikePaymentMethodHandler(
        TronUSDTLikeSpecificBtcPayNetwork network,
        TronUSDTRPCProvider tronUSDTRpcProvider,
        InvoiceRepository invoiceRepository,
        StoreRepository storeRepository)
    {
        PaymentMethodId = TronUSDTLike.GetPaymentMethodId(network.CryptoCode);
        Network = network;
        Serializer = BlobSerializer.CreateSerializer().Serializer;
        _tronUSDTRpcProvider = tronUSDTRpcProvider;
        _invoiceRepository = invoiceRepository;
        _storeRepository = storeRepository;
    }

    internal static PaymentType TronUSDTLike => TronUSDTPaymentType.Instance;
    public TronUSDTLikeSpecificBtcPayNetwork Network { get; }

    public JsonSerializer Serializer { get; }

    public PaymentMethodId PaymentMethodId { get; }

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
            .GetOneNotReservedAddress(context.PaymentMethodId, _invoiceRepository);
        if (availableAddress == null)
            throw new PaymentMethodUnavailableException("All your TRON adresses are currently waiting payment");
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
        return ParsePaymentPromptDetails(details);
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

    public TronUSDTLikeOnChainPaymentMethodDetails ParsePaymentPromptDetails(JToken details)
    {
        return details.ToObject<TronUSDTLikeOnChainPaymentMethodDetails>(Serializer);
    }

    public TronUSDTLikePaymentData ParsePaymentDetails(JToken details)
    {
        return details.ToObject<TronUSDTLikePaymentData>(Serializer) ??
               throw new FormatException($"Invalid {nameof(TronUSDTLikePaymentMethodHandler)}");
    }
}