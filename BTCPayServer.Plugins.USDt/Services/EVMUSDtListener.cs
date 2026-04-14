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
using Nethereum.RPC.Eth.DTOs;

namespace BTCPayServer.Plugins.USDt.Services;

public class EVMUSDtListener(
    InvoiceRepository invoiceRepository,
    ISettingsRepository settingsRepository,
    EventAggregator eventAggregator,
    EVMUSDtRPCProvider rpcProvider,
    USDtPluginConfiguration usdtPluginConfiguration,
    ILogger<EVMUSDtListener> logger,
    PaymentMethodHandlerDictionary handlers,
    PaymentService paymentService)
    : USDtListener<EVMUSDtLikeConfigurationItem, EVMUSDtPaymentData>(
        invoiceRepository,
        settingsRepository,
        eventAggregator,
        rpcProvider,
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

    protected override IDisposable? BeginLoggingScope(PaymentMethodId paymentMethodId)
    {
        return _logger.BeginScope("EVM PMI: {PaymentMethodId}", paymentMethodId);
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
                    (object?)null,
                    (object)destinationBatch,
                    new BlockParameter(block.Number),
                    new BlockParameter(block.Number));
                var part = await transferEvent.GetAllChangesAsync(filter);
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
                    change.Log.Removed)),
                invoicesPerAddress.Keys)
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
        IEnumerable<string> destinationKeys)
    {
        var destinationKeySet = destinationKeys
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        return changes
            .Where(change => !change.Removed)
            .Select(change => new TransferMatchSnapshot(
                change.To.ToLowerInvariant(),
                change.From,
                change.To,
                change.Value,
                $"{change.TransactionHash.Replace("0x", "")}-{change.TransactionIndex}"))
            .Where(match => destinationKeySet.Contains(match.DestinationKey))
            .DistinctBy(match => match.TransactionId)
            .ToArray();
    }

    internal sealed record TransferLogSnapshot(
        string To,
        string From,
        BigInteger Value,
        string TransactionHash,
        string TransactionIndex,
        bool Removed);

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