using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.Ethereum;
using BTCPayServer.Plugins.USDt.Services.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBXplorer;
using Nethereum.BlockchainProcessing.BlockStorage.Entities.Mapping;
using Nethereum.Contracts;
using Nethereum.Contracts.Standards.ERC20.ContractDefinition;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;

namespace BTCPayServer.Plugins.USDt.Services;

public class EthUSDtListener(
    InvoiceRepository invoiceRepository,
    ISettingsRepository settingsRepository,
    EventAggregator eventAggregator,
    EthUSDtRPCProvider rpcProvider,
    USDtPluginConfiguration usdtPluginConfiguration,
    ILogger<EthUSDtListener> logger,
    PaymentMethodHandlerDictionary handlers,
    PaymentService paymentService) : IHostedService
{
    public static readonly List<InvoiceStatus> StatusToTrack =
    [
        InvoiceStatus.New,
        InvoiceStatus.Processing
    ];

    private readonly CompositeDisposable _leases = new();
    private CancellationTokenSource? _cts;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (usdtPluginConfiguration.EthereumUSDtLikeConfigurationItems.Count == 0) return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        foreach (var kv in usdtPluginConfiguration.EthereumUSDtLikeConfigurationItems)
            _ = LoopIndex(kv.Value, _cts.Token);
        return Task.CompletedTask;
    }


    public Task StopAsync(CancellationToken cancellationToken)
    {
        _leases.Dispose();
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task LoopIndex(EthUSDtLikeConfigurationItem configurationItem, CancellationToken stoppingToken)
    {
        var rateLimitBackoffMs = 5_000;
        while (stoppingToken.IsCancellationRequested == false)
            try
            {
                var listenerState = await LoadTrackingState(configurationItem);
                var pmi = configurationItem.GetPaymentMethodId();
                using var _ = logger.BeginScope("ETH PMI: {PaymentMethodId}", pmi);

                var web3Client = rpcProvider.GetWeb3Client(pmi);
                if (listenerState == null)
                {
                    logger.LogInformation("No tracking state found, new blockchain");

                    var latestBlockNumber = await web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync(
                        BlockParameter.CreateLatest());

                    listenerState = new EVMBasedListenerState { LastBlockHeight = (long)latestBlockNumber.Value - 1 };
                    await SetTrackingState(configurationItem, listenerState);
                }
                else
                {
                    var latestBlockNumber = await web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync(
                        BlockParameter.CreateLatest());

                    logger.LogInformation(
                        "Tracking state, current={CurrentBlockNumber}, latest={LatestBlockNumber}",
                        listenerState.LastBlockHeight, latestBlockNumber);
                }

                while (!stoppingToken.IsCancellationRequested)
                {
                    if ((await invoiceRepository.GetMonitoredInvoices(pmi, true, stoppingToken)).Any(i =>
                            StatusToTrack.Any(s => s == i.Status)) ==
                        false)
                    {
                        var lastBlockNumber = await web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync();
                        if (lastBlockNumber.Value > listenerState.LastBlockHeight)
                        {
                            logger.LogInformation("New block avoid from {BlockNumber} to {NewBlockNumber}",
                                listenerState.LastBlockHeight, lastBlockNumber.Value);
                            listenerState.LastBlockHeight = (long)lastBlockNumber.Value;
                        }

                        await Task.Delay(30_000, stoppingToken);
                    }
                    else
                    {
                        var block =
                            await web3Client.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(
                                new BlockParameter(
                                    new HexBigInteger(listenerState.LastBlockHeight + new BigInteger(1))));

                        if (block != null)
                        {
                            await OnNewBlockToIndex(pmi, block, stoppingToken);
                            logger.LogInformation("New block indexed {BlockNumber}", block.Number);

                            listenerState.LastBlockHeight = (long)block.Number.Value;
                        }
                        else
                        {
                            logger.LogInformation("Block not present on node yet {BlockNumber}",
                                listenerState.LastBlockHeight);
                            await Task.Delay(1_000, stoppingToken);
                        }
                    }


                    await SetTrackingState(configurationItem, listenerState);
                    rateLimitBackoffMs = 5_000; // reset after successful iteration
                }
            }
            catch (RpcClientTimeoutException)
            {
                logger.LogWarning("Timeout while indexing, is the node running? Retrying in 10 seconds");
                await Task.Delay(5_000, stoppingToken);
            }
            catch (RpcClientUnknownException e) when (e.InnerException?.Message?.Contains("429 (Too Many Requests)") ==
                                                      true)
            {
                logger.LogWarning(
                    "Rate limit exceeded while indexing, use an Ethereum node with higher limits if possible. Retrying in {DelayMs} ms",
                    rateLimitBackoffMs);
                await Task.Delay(rateLimitBackoffMs, stoppingToken);
                rateLimitBackoffMs = Math.Min(rateLimitBackoffMs * 2, 60_000);
            }
            catch (Exception e)
            {
                logger.LogError(e, "An error occurred while indexing");
                await Task.Delay(10_000, stoppingToken);
            }
    }

    private async Task SetTrackingState(EthUSDtLikeConfigurationItem config, EVMBasedListenerState trackingState)
    {
        await settingsRepository.UpdateSetting(trackingState, EthUSDtRPCProvider.ListenerStateSettingKey(config));
    }

    private async Task<EVMBasedListenerState?> LoadTrackingState(EthUSDtLikeConfigurationItem config)
    {
        return await settingsRepository.GetSettingAsync<EVMBasedListenerState>(
            EthUSDtRPCProvider.ListenerStateSettingKey(config));
    }

    private Task ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment)
    {
        logger.LogInformation(
            $"Invoice {invoice.Id} received payment {payment.Value} {payment.Currency} {payment.Id}");

        eventAggregator.Publish(
            new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) { Payment = payment });
        return Task.CompletedTask;
    }

    private EthUSDtLikeConfigurationItem GetConfig(PaymentMethodId pmi)
    {
        return usdtPluginConfiguration.EthereumUSDtLikeConfigurationItems[pmi];
    }

    private async Task UpdatePaymentStates(PaymentMethodId pmi, InvoiceEntity[] invoices, BlockWithTransactions block,
        CancellationToken stoppingToken)
    {
        if (invoices.Length == 0) return;

        var handler = (EthUSDtPaymentMethodHandler)handlers[pmi];

        var expandedInvoices = invoices.Select(entity => (Invoice: entity,
                Prompt: entity.GetPaymentPrompt(pmi),
                PaymentMethodDetails: handler.ParsePaymentPromptDetails(entity.GetPaymentPrompt(pmi)!.Details)))
            .Select(tuple => (
                tuple.Invoice,
                tuple.PaymentMethodDetails,
                tuple.Prompt
            )).ToArray();

        var invoicesPerAddress = expandedInvoices.Where(i => i.Prompt is { Destination: not null })
            .ToDictionary(i => i.Prompt!.Destination!.ToLowerInvariant(), i => i);

        var web3Client = rpcProvider.GetWeb3Client(pmi);
        var transferEvent =
            web3Client.Eth.GetEvent<TransferEventDTO>(GetConfig(pmi).SmartContractAddress.ToLowerInvariant());

        // Build batched topic filter on destination addresses to keep logs light
        var toAddresses = invoicesPerAddress.Keys.ToArray();
        var changes = new List<EventLog<TransferEventDTO>>();
        var tries = 0;
        do
        {
            changes.Clear();
            const int batchSize = 1;
            for (var i = 0; i < toAddresses.Length; i += batchSize)
            {
                var toAddress = toAddresses[i];
                // Use overload: CreateFilterInput(firstIndexed, secondIndexed, fromBlock, toBlock)
                // ERC20 Transfer has two indexed params: from (topic1), to (topic2). We wildcard 'from' and filter on single 'to'.
                var filter = transferEvent.CreateFilterInput(
                    (object?)null,
                    (object)toAddress,
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

        if (changes == null)
            throw new InvalidOperationException($"Unable to get changes {block.Number}");

        var matches = changes
            .Where(t => t.Log.Removed == false)
            .Where(t => invoicesPerAddress.ContainsKey(t.Event.To.ToLowerInvariant()));

        foreach (var t in matches)
        {
            var (invoice, _, _) =
                invoicesPerAddress[t.Event.To.ToLowerInvariant()];
            await HandlePaymentData(pmi, t.Event.From,
                t.Event.To,
                t.Event.Value,
                $"{t.Log.TransactionHash.Replace("0x", "")}-{t.Log.TransactionIndex}", 0, block.Number.ToLong(),
                invoice);
        }

        var updatedPaymentEntities = new List<(PaymentEntity Payment, InvoiceEntity invoice)>();
        foreach (var invoice in invoices)
        foreach (var payment in GetPendingPayments(invoice, pmi))
        {
            var paymentData = handler.ParsePaymentDetails(payment.Details);
            var confDiff = block.Number.Value - paymentData.BlockHeight;
            var confs = confDiff < 0 ? 0 : (long)confDiff;
            paymentData.ConfirmationCount = confs > int.MaxValue ? int.MaxValue : (int)confs;

            payment.Status = paymentData.PaymentConfirmed(invoice.SpeedPolicy)
                ? PaymentStatus.Settled
                : PaymentStatus.Processing;
            payment.SetDetails(handler, paymentData);

            updatedPaymentEntities.Add((payment, invoice));
        }

        await paymentService.UpdatePayments(updatedPaymentEntities.Select(tuple => tuple.Payment).ToList());
        foreach (var valueTuples in updatedPaymentEntities.GroupBy(entity => entity.invoice))
            if (valueTuples.Any())
                eventAggregator.Publish(new InvoiceNeedUpdateEvent(valueTuples.Key.Id));
    }

    private async Task OnNewBlockToIndex(PaymentMethodId pmi, BlockWithTransactions block,
        CancellationToken stoppingToken)
    {
        await UpdateAnyPendingPayment(pmi, block, stoppingToken);
    }

    private async Task HandlePaymentData(
        PaymentMethodId pmi,
        string from,
        string to,
        BigInteger totalAmount,
        string txId, int confirmations, long blockHeight, InvoiceEntity invoice)
    {
        var totalAmountBigDecimal = decimal.Parse(
            Web3.Convert.FromWeiToBigDecimal(totalAmount, GetConfig(pmi).Divisibility).ToString(),
            CultureInfo.InvariantCulture);

        var config = GetConfig(pmi);
        var handler = (EthUSDtPaymentMethodHandler)handlers[pmi];
        EthUSDtPaymentData details = new()
        {
            To = to,
            From = from,
            TransactionId = txId,
            ConfirmationCount = confirmations,
            BlockHeight = blockHeight
        };

        var paymentData = new PaymentData
        {
            Status = details.PaymentConfirmed(invoice.SpeedPolicy) ? PaymentStatus.Settled : PaymentStatus.Processing,
            Amount = totalAmountBigDecimal,
            Created = DateTimeOffset.UtcNow,
            Id = txId,
            Currency = config.Currency,
            InvoiceDataId = invoice.Id
        }.Set(invoice, handler, details);

        var payment = await paymentService.AddPayment(paymentData, [txId]);
        if (payment != null)
            await ReceivedPayment(invoice, payment);
    }


    private async Task UpdateAnyPendingPayment(PaymentMethodId pmi, BlockWithTransactions block,
        CancellationToken stoppingToken)
    {
        var invoices = (await invoiceRepository.GetMonitoredInvoices(pmi, true))
            .Where(i => StatusToTrack.Contains(i.Status))
            .Where(i => i.GetPaymentPrompt(pmi)?.Activated is true)
            .ToArray();

        if (invoices.Length == 0)
            return;

        await UpdatePaymentStates(pmi, invoices, block, stoppingToken);
    }

    private static IEnumerable<PaymentEntity> GetPendingPayments(InvoiceEntity invoice, PaymentMethodId pmi)
    {
        return invoice.GetPayments(false)
            .Where(p => p.PaymentMethodId == pmi)
            .Where(p => p.Status == PaymentStatus.Processing);
    }
}