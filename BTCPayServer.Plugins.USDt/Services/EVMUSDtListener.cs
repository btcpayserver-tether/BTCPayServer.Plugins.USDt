using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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

        return ToTransferMatchSnapshots(
                changes.Select(change => new TransferLogSnapshot(
                    change.Event.To,
                    change.Event.From,
                    change.Event.Value,
                    change.Log.TransactionHash,
                    change.Log.TransactionIndex?.ToString() ?? string.Empty,
                    change.Log.Removed,
                    change.Log.LogIndex?.Value)),
                invoicesPerAddress.Keys,
                invoicesPerAddress.Values.SelectMany(invoice => invoice.GetPayments(false))
                    .Where(payment => payment.PaymentMethodId == paymentMethodId)
                    .Select(payment =>
                    {
                        var details = (EVMUSDtPaymentData)handlers[paymentMethodId].ParsePaymentDetails(payment.Details);
                        return new ExistingTransferSnapshot(details.TransactionId, details.To,
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

    internal static IReadOnlyCollection<TransferMatchSnapshot> ToTransferMatchSnapshots(
        IEnumerable<TransferLogSnapshot> changes,
        IEnumerable<string> destinationKeys,
        IEnumerable<ExistingTransferSnapshot>? existingTransfers = null)
    {
        var destinationKeySet = destinationKeys
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var existing = new Dictionary<string, ExistingTransferSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var payment in existingTransfers ?? [])
        {
            if (existing.TryGetValue(payment.TransactionId, out var duplicate))
            {
                // Identical repeated entries are harmless, but two distinct stored IDs
                // differing only in case cannot safely be treated as one DB payment.
                if (duplicate.TransactionId != payment.TransactionId || duplicate.Value != payment.Value ||
                    !string.Equals(duplicate.To, payment.To, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Conflicting stored ERC-20 payment ID: {payment.TransactionId}.");
            }
            else
                existing.Add(payment.TransactionId, payment);
        }
        // New IDs must not depend on which invoice destinations remain tracked.
        // Only reuse a legacy ID when it is already stored for a matching log.
        return changes
            .Where(change => !change.Removed)
            .Select(change => change with { TransactionHash = change.TransactionHash.ToLowerInvariant() })
            .OrderBy(change => change.LogIndex)
            .GroupBy(change => change.TransactionHash)
            .SelectMany(group =>
            {
                if (group.Any(change => change.LogIndex is null || change.LogIndex < 0))
                    throw new InvalidOperationException("ERC-20 transfer log has no valid log index.");
                var logs = group.DistinctBy(change => change.LogIndex).ToArray();
                if (group.Any(change => change != logs.Single(log => log.LogIndex == change.LogIndex)))
                    throw new InvalidOperationException("Conflicting ERC-20 transfer logs have the same log index.");
                var legacyId = $"{logs[0].TransactionHash.Replace("0x", "")}-{logs[0].TransactionIndex}";
                var legacyIndex = -1;
                if (existing.TryGetValue(legacyId, out var previous))
                {
                    legacyIndex = Array.FindIndex(logs, log =>
                        string.Equals(log.To, previous.To, StringComparison.OrdinalIgnoreCase) && log.Value == previous.Value &&
                        !existing.ContainsKey($"{legacyId}-log-{log.LogIndex}"));
                    if (legacyIndex < 0)
                        throw new InvalidOperationException($"Stored ERC-20 payment {previous.TransactionId} does not match the returned transfer logs.");
                }
                return logs.Select((change, index) =>
                {
                    var id = index == legacyIndex ? previous!.TransactionId : $"{legacyId}-log-{change.LogIndex}";
                    if (existing.TryGetValue(id, out var stored))
                    {
                        if (stored.Value != change.Value || !string.Equals(stored.To, change.To, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"Stored ERC-20 payment {stored.TransactionId} does not match the returned transfer log.");
                        id = stored.TransactionId;
                    }
                    return new TransferMatchSnapshot(change.To.ToLowerInvariant(), change.From, change.To, change.Value, id);
                });
            })
            .Where(match => destinationKeySet.Contains(match.DestinationKey))
            .DistinctBy(match => match.TransactionId)
            .ToArray();
    }

    internal static bool IsBlockRangeBeyondCurrentHeadError(Exception exception)
    {
        return exception.Message.Contains("block range extends beyond current head block",
                   StringComparison.OrdinalIgnoreCase) &&
               exception.Message.Contains("eth_getLogs", StringComparison.OrdinalIgnoreCase);
    }

    internal sealed record TransferLogSnapshot(
        string To,
        string From,
        BigInteger Value,
        string TransactionHash,
        string TransactionIndex,
        bool Removed,
        BigInteger? LogIndex = null);

    internal sealed record ExistingTransferSnapshot(string TransactionId, string To, BigInteger Value);

    internal sealed record TransferMatchSnapshot(
        string DestinationKey,
        string From,
        string To,
        BigInteger TotalAmount,
        string TransactionId);

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
