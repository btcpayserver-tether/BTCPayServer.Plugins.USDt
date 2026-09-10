using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.EVM;
using BTCPayServer.Plugins.USDt.Services.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Logging;
using Nethereum.Contracts;
using Nethereum.Contracts.Standards.ERC20.ContractDefinition;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.RPC.Eth.DTOs;

namespace BTCPayServer.Plugins.USDt.Services;

public class EVMUSDtListener(
    ISettingsRepository settingsRepository,
    EventAggregator eventAggregator,
    EVMUSDtRPCProvider rpcProvider,
    USDtPluginConfiguration usdtPluginConfiguration,
    USDtChainActivationService activationService,
    USDtTrackedInvoiceProvider trackedInvoiceProvider,
    ILogger<EVMUSDtListener> logger,
    PaymentMethodHandlerDictionary handlers,
    PaymentService paymentService)
    : USDtListener<EVMUSDtLikeConfigurationItem, EVMUSDtPaymentData>(
        settingsRepository,
        eventAggregator,
        rpcProvider,
        activationService,
        trackedInvoiceProvider,
        logger,
        handlers,
        paymentService)
{
    internal const int DestinationFilterBatchSize = 20;

    private readonly ILogger<EVMUSDtListener> _logger = logger;
    private readonly USDtTrackedInvoiceProvider _trackedInvoiceProvider = trackedInvoiceProvider;

    protected override IReadOnlyDictionary<PaymentMethodId, EVMUSDtLikeConfigurationItem> GetConfigurations()
    {
        return usdtPluginConfiguration.EVMUSDtLikeConfigurationItems
            .Where(pair => pair.Value.HasValidSmartContractAddress())
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    protected override string GetListenerStateSettingKey(EVMUSDtLikeConfigurationItem config)
    {
        return EVMUSDtRPCProvider.ListenerStateSettingKey(config);
    }

    protected override string RateLimitNodeName => "EVM";

    protected override bool UseExponentialRateLimitBackoff => true;

    protected override long GetHeadLagBlocks(EVMUSDtLikeConfigurationItem configurationItem)
    {
        return configurationItem.Chain is Constants.PolygonChainName or Constants.AmoyChainName ? 2 : 1;
    }

    protected override IDisposable? BeginLoggingScope(PaymentMethodId paymentMethodId)
    {
        var chain = usdtPluginConfiguration.EVMUSDtLikeConfigurationItems.TryGetValue(paymentMethodId, out var configuration)
            ? configuration.Chain
            : paymentMethodId.ToString();

        return _logger.BeginScope("EVM PMI: {PaymentMethodId}, Chain: {Chain}", paymentMethodId, chain);
    }

    protected override async Task<IReadOnlyCollection<USDtTransferMatch>> GetTransfersAsync(
        PaymentMethodId paymentMethodId,
        BlockWithTransactions block,
        IReadOnlyDictionary<string, InvoiceEntity> invoicesPerAddress,
        CancellationToken stoppingToken)
    {
        var configuration = usdtPluginConfiguration.EVMUSDtLikeConfigurationItems[paymentMethodId];
        if (!configuration.HasValidSmartContractAddress())
            return [];

        var web3Client = rpcProvider.GetWeb3Client(paymentMethodId);
        var transferEvent = web3Client.Eth.GetEvent<TransferEventDTO>(configuration.SmartContractAddress.ToLowerInvariant());
        List<EventLog<TransferEventDTO>> changes = [];
        var destinationBatches = BatchDestinationAddresses(invoicesPerAddress.Keys);
        var tries = 0;
        do
        {
            changes.Clear();
            foreach (var destinationBatch in destinationBatches)
            {
                var filter = transferEvent.CreateFilterInput(
                    (object[]?)null,
                    (object[])destinationBatch,
                    new BlockParameter(block.Number),
                    new BlockParameter(block.Number));
                List<EventLog<TransferEventDTO>> part;
                try
                {
                    part = await transferEvent.GetAllChangesAsync(filter);
                }
                catch (RpcResponseException e) when (IsBlockRangeBeyondCurrentHeadError(e))
                {
                    throw new USDtListenerTransientException(
                        $"eth_getLogs is not ready for {paymentMethodId} block {(long)block.Number.Value}",
                        e);
                }

                if (part != null && part.Count != 0)
                    changes.AddRange(part);
            }

            if (changes.Count != 0)
                break;

            await Task.Delay(250, stoppingToken);
        } while (tries++ < 3);

        var matchedChanges = changes
            .Where(change => !change.Log.Removed &&
                             invoicesPerAddress.ContainsKey(change.Event.To.ToLowerInvariant()))
            .ToArray();
        if (matchedChanges.Length == 0)
            return [];

        // Monitored expired invoices can contain only pending payments. Replay needs
        // settled payments too, including legacy IDs and already credited log IDs.
        var invoiceIds = matchedChanges
            .Select(change => invoicesPerAddress[change.Event.To.ToLowerInvariant()].Id)
            .Distinct()
            .ToArray();
        var invoicesWithPayments = await _trackedInvoiceProvider.GetInvoicesWithPayments(invoiceIds, stoppingToken);

        return USDtTransferMatcher.ToTransferMatchSnapshots(
                matchedChanges.Select(change => new USDtTransferMatcher.TransferLogSnapshot(
                    change.Event.To,
                    change.Event.From,
                    change.Event.Value,
                    change.Log.TransactionHash,
                    change.Log.TransactionIndex?.ToString() ?? string.Empty,
                    change.Log.Removed,
                    change.Log.LogIndex?.Value)),
                invoicesPerAddress.Keys,
                invoicesWithPayments.SelectMany(invoice => invoice.GetPayments(false))
                    .Where(payment => payment.PaymentMethodId == paymentMethodId)
                    .Select(payment =>
                    {
                        var details = (EVMUSDtPaymentData)handlers[paymentMethodId].ParsePaymentDetails(payment.Details);
                        return new USDtTransferMatcher.ExistingTransferSnapshot(details.TransactionId, details.To,
                            Nethereum.Web3.Web3.Convert.ToWei(payment.Value, configuration.Divisibility));
                    }))
            .Select(match => new USDtTransferMatch(
                match.DestinationKey,
                match.From,
                match.To,
                match.TotalAmount,
                match.TransactionId))
            .ToArray();
    }

    internal static IReadOnlyList<string[]> BatchDestinationAddresses(
        IEnumerable<string> destinationKeys,
        int batchSize = DestinationFilterBatchSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        return destinationKeys
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Chunk(batchSize)
            .ToArray();
    }

    internal static bool IsBlockRangeBeyondCurrentHeadError(Exception exception)
    {
        return exception.Message.Contains("block range extends beyond current head block",
                   StringComparison.OrdinalIgnoreCase) &&
               exception.Message.Contains("eth_getLogs", StringComparison.OrdinalIgnoreCase);
    }

    protected override EVMUSDtPaymentData CreatePaymentDetails(
        string from,
        string to,
        string transactionId,
        int confirmations,
        long blockHeight)
    {
        return new EVMUSDtPaymentData
        {
            To = to,
            From = from,
            TransactionId = transactionId,
            ConfirmationCount = confirmations,
            BlockHeight = blockHeight
        };
    }
}
