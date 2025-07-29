using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
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
using BTCPayServer.Plugins.USDt.Configuration.Tron;
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

public class TronUSDtListener(
    InvoiceRepository invoiceRepository,
    ISettingsRepository settingsRepository,
    EventAggregator eventAggregator,
    TronUSDtRPCProvider tronUSDtRpcProvider,
    USDtPluginConfiguration usdtPluginConfiguration,
    ILogger<TronUSDtListener> logger,
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
        if (usdtPluginConfiguration.TronUSDtLikeConfigurationItems.Count == 0) return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = LoopIndex(usdtPluginConfiguration.TronUSDtLikeConfigurationItems.Single().Value, _cts.Token);
        return Task.CompletedTask;
    }


    public Task StopAsync(CancellationToken cancellationToken)
    {
        _leases.Dispose();
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task LoopIndex(TronUSDtLikeConfigurationItem configurationItem, CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested == false)
            try
            {
                var listenerState = await LoadTrackingState(configurationItem);
                var pmi = configurationItem.GetPaymentMethodId();
                
                var web3Client = tronUSDtRpcProvider.GetWeb3Client(pmi);
                if (listenerState == null)
                {
                    logger.LogInformation("No tracking state found, new blockchain");

                    var latestBlockNumber = await web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync(
                        BlockParameter.CreateLatest());

                    listenerState = new TronUSDtListenerState { LastBlockHeight = latestBlockNumber.Value };
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
                    if ((await invoiceRepository.GetMonitoredInvoices(pmi, true, cancellationToken: stoppingToken)).Any(i => StatusToTrack.Any(s => s == i.Status)) ==
                        false)
                    {
                        var lastBlockNumber = await web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync();
                        if (lastBlockNumber > listenerState.LastBlockHeight)
                        {
                            logger.LogInformation("New block avoid from {BlockNumber} to {NewBlockNumber}",
                                listenerState.LastBlockHeight, lastBlockNumber);
                            listenerState.LastBlockHeight = lastBlockNumber;
                        }

                        Thread.Sleep(30_000);
                    }
                    else
                    {
                        var block =
                            await web3Client.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(
                                new BlockParameter((listenerState.LastBlockHeight + 1).ToHexBigInteger()));

                        if (block != null)
                        {
                            await OnNewBlockToIndex(pmi, block);
                            logger.LogInformation("New block indexed {BlockNumber}", block.Number);

                            listenerState.LastBlockHeight = block.Number.Value;
                        }
                        else
                        {
                            logger.LogInformation("Block not present on node yet {BlockNumber}", listenerState.LastBlockHeight);
                            Thread.Sleep(1_000);
                        }
                    }


                    await SetTrackingState(configurationItem, listenerState);
                }
            }
            catch(RpcClientTimeoutException e)
            {
                logger.LogWarning("Timeout while indexing, is the node running? Retrying in 10 seconds");
                Thread.Sleep(5_000);
            }
            catch(RpcClientUnknownException e) when (e.InnerException?.Message?.Contains("429 (Too Many Requests)") == true)
            {
                logger.LogWarning("Rate limit exceeded while indexing, use a Tron node with higher limits if possible. Retrying in 10 seconds");
                Thread.Sleep(10_000);
            }
            catch (Exception e)
            {
                logger.LogError(e, "An error occurred while indexing");
                Thread.Sleep(10_000);
            }
    }

    private async Task SetTrackingState(TronUSDtLikeConfigurationItem config, TronUSDtListenerState trackingState)
    {
        await settingsRepository.UpdateSetting(trackingState, TronUSDtRPCProvider.ListenerStateSettingKey(config));
    }

    private async Task<TronUSDtListenerState?> LoadTrackingState(TronUSDtLikeConfigurationItem config)
    {
        return await settingsRepository.GetSettingAsync<TronUSDtListenerState>(TronUSDtRPCProvider.ListenerStateSettingKey(config));
    }

    private Task ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment)
    {
        logger.LogInformation(
            $"Invoice {invoice.Id} received payment {payment.Value} {payment.Currency} {payment.Id}");

        eventAggregator.Publish(
            new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) { Payment = payment });
        return Task.CompletedTask;
    }

    private TronUSDtLikeConfigurationItem GetConfig(PaymentMethodId pmi)
    {
        return usdtPluginConfiguration.TronUSDtLikeConfigurationItems[pmi];
    }

    private async Task UpdatePaymentStates(PaymentMethodId pmi, InvoiceEntity[] invoices, BlockWithTransactions block)
    {
        if (invoices.Length == 0) return;

        var handler = (TronUSDtLikePaymentMethodHandler)handlers[pmi];

        var expandedInvoices = invoices.Select(entity => (Invoice: entity,
                Prompt: entity.GetPaymentPrompt(pmi),
                PaymentMethodDetails: handler.ParsePaymentPromptDetails(entity.GetPaymentPrompt(pmi)!.Details)))
            .Select(tuple => (
                tuple.Invoice,
                tuple.PaymentMethodDetails,
                tuple.Prompt
            )).ToArray();

        var invoicesPerAddress = expandedInvoices.Where(i => i.Prompt is { Destination: not null })
            .ToDictionary(i => i.Prompt!.Destination.ToLowerInvariant(), i => i);

        var web3Client = tronUSDtRpcProvider.GetWeb3Client(pmi);
        var transferEvent = web3Client.Eth.GetEvent<TransferEventDTO>();

        // This is a workaround for the fact that sometimes the event is not indexed yet
        List<EventLog<TransferEventDTO>>? changes;
        int tries = 0;
        do
        {
               changes = await transferEvent.GetAllChangesAsync(
                    transferEvent.CreateFilterInput(new BlockParameter(block.Number),
                        new BlockParameter(block.Number)));
               
               if (changes != null && changes.Count != 0)
                   break;
               
               Thread.Sleep(250);
        } while (tries++ < 3);
        
        if(changes == null)
            throw new InvalidOperationException($"Unable to get changes {block.Number}");

        var matches = changes
            .Where(t => t.Log.Removed == false && TronUSDtAddressHelper.HexToBase58(t.Log.Address)
                .Equals(GetConfig(pmi).SmartContractAddress, StringComparison.InvariantCultureIgnoreCase))
            .Where(t => invoicesPerAddress.ContainsKey(TronUSDtAddressHelper.HexToBase58(t.Event.To)
                .ToLowerInvariant()));

        foreach (var t in matches)
        {
            var (invoice, _, _) =
                invoicesPerAddress[TronUSDtAddressHelper.HexToBase58(t.Event.To).ToLowerInvariant()];
            await HandlePaymentData(pmi, TronUSDtAddressHelper.HexToBase58(t.Event.From),
                TronUSDtAddressHelper.HexToBase58(t.Event.To),
                t.Event.Value.ToHexBigInteger().ToLong(),
                $"{t.Log.TransactionHash.Replace("0x", "")}-{t.Log.TransactionIndex}", 0, block.Number.ToLong(),
                invoice);
        }

        var updatedPaymentEntities = 
            new BlockingCollection<(PaymentEntity Payment, InvoiceEntity invoice)>();
        foreach (var invoice in invoices)
            foreach (var payment in GetPendingTronUSDtLikePayments(invoice, pmi))
            {
                var paymentData = handler.ParsePaymentDetails(payment.Details);
                paymentData.ConfirmationCount = (int)(block.Number.Value - paymentData.BlockHeight);
                
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

    private async Task OnNewBlockToIndex(PaymentMethodId pmi, BlockWithTransactions block)
    {
        await UpdateAnyPendingTronUSDtLikePayment(pmi, block);
    }

    private async Task HandlePaymentData(
        PaymentMethodId pmi,
        string from,
        string to,
        BigInteger totalAmount,
        string txId, int confirmations, long blockHeight, InvoiceEntity invoice)
    {
        var totalAmountBigDecimal = decimal.Parse(Web3.Convert.FromWeiToBigDecimal(totalAmount, GetConfig(pmi).Divisibility).ToString(),
            CultureInfo.InvariantCulture);
        
        var config = GetConfig(pmi);
        var handler = (TronUSDtLikePaymentMethodHandler)handlers[pmi];
        TronUSDtLikePaymentData details = new()
        {
            To = to,
            From = from,
            TransactionId = txId,
            ConfirmationCount = confirmations,
            BlockHeight = blockHeight,
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


    private async Task UpdateAnyPendingTronUSDtLikePayment(PaymentMethodId pmi, BlockWithTransactions block)
    {
        var invoices = (await invoiceRepository.GetMonitoredInvoices(pmi, true))
            .Where(i => StatusToTrack.Contains(i.Status))
            .Where(i => i.GetPaymentPrompt(pmi)?.Activated is true)
            .ToArray();
        
        if (invoices.Length == 0)
            return;

        await UpdatePaymentStates(pmi, invoices, block);
    }

    private static IEnumerable<PaymentEntity> GetPendingTronUSDtLikePayments(InvoiceEntity invoice, PaymentMethodId pmi)
    {
        return invoice.GetPayments(false)
            .Where(p => p.PaymentMethodId == pmi)
            .Where(p => p.Status == PaymentStatus.Processing);
    }
}