using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
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
using BTCPayServer.Plugins.USDt.Services.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBXplorer;
using Nethereum.BlockchainProcessing.BlockStorage.Entities.Mapping;
using Nethereum.Contracts;
using Nethereum.Contracts.Standards.ERC20.ContractDefinition;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;

namespace BTCPayServer.Plugins.USDt.Services;

public class TronUSDtListener(
    InvoiceRepository invoiceRepository,
    ISettingsRepository settingsRepository,
    EventAggregator eventAggregator,
    TronUSDtRPCProvider tronUSDtRpcProvider,
    TronUSDtLikeConfiguration tronUSDtLikeConfiguration,
    BTCPayNetworkProvider networkProvider,
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
        if (tronUSDtLikeConfiguration.TronUSDtLikeConfigurationItems.Count == 0) return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = LoopIndex(tronUSDtLikeConfiguration.TronUSDtLikeConfigurationItems.Keys.Single(), _cts.Token);
        return Task.CompletedTask;
    }


    public Task StopAsync(CancellationToken cancellationToken)
    {
        _leases.Dispose();
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task LoopIndex(string cryptoCode, CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested == false)
            try
            {
                var listenerState = await LoadTrackingState(cryptoCode);
                var web3Client = tronUSDtRpcProvider.GetWeb3Client(cryptoCode);
                if (listenerState == null)
                {
                    logger.LogInformation("No tracking state found, new blockchain");

                    var latestBlockNumber = await web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync(
                        BlockParameter.CreateLatest());

                    listenerState = new TronUSDtListenerState { LastBlockHeight = latestBlockNumber.Value };
                    await SetTrackingState(cryptoCode, listenerState);
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
                    var paymentMethodId = TronUSDtPaymentType.Instance.GetPaymentMethodId(cryptoCode);
                    if ((await invoiceRepository.GetMonitoredInvoices(paymentMethodId, true, cancellationToken: stoppingToken)).Any(i => StatusToTrack.Any(s => s == i.Status)) ==
                        false)
                    {
                        var lastBlockNumber = await web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync();
                        if (lastBlockNumber > listenerState.LastBlockHeight)
                        {
                            eventAggregator.Publish(new NewBlockEvent { CryptoCode = cryptoCode });

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
                            await OnNewBlockToIndex(cryptoCode, block);
                            logger.LogInformation("New block indexed {BlockNumber}", block.Number);

                            listenerState.LastBlockHeight = block.Number.Value;
                        }
                        else
                        {
                            logger.LogInformation("Block not present on node yet {BlockNumber}", listenerState.LastBlockHeight);
                            Thread.Sleep(500);
                        }
                    }


                    await SetTrackingState(cryptoCode, listenerState);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "An error occurred while indexing");
                Thread.Sleep(10_000);
            }
    }

    private async Task SetTrackingState(string cryptoCode, TronUSDtListenerState trackingState)
    {
        await settingsRepository.UpdateSetting(trackingState, TronUSDtRPCProvider.ListenerStateSettingKey(cryptoCode));
    }

    private async Task<TronUSDtListenerState?> LoadTrackingState(string cryptoCode)
    {
        return await settingsRepository.GetSettingAsync<TronUSDtListenerState>(
            TronUSDtRPCProvider.ListenerStateSettingKey(cryptoCode));
    }

    private Task ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment)
    {
        logger.LogInformation(
            $"Invoice {invoice.Id} received payment {payment.Value} {payment.Currency} {payment.Id}");

        eventAggregator.Publish(
            new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) { Payment = payment });
        return Task.CompletedTask;
    }

    private TronUSDtLikeConfigurationItem GetConfig(string cryptoCode)
    {
        return tronUSDtLikeConfiguration.TronUSDtLikeConfigurationItems[cryptoCode];
    }

    private async Task UpdatePaymentStates(string cryptoCode, InvoiceEntity[] invoices, BlockWithTransactions block)
    {
        if (invoices.Length == 0) return;

        var network = networkProvider.GetNetwork(cryptoCode);
        var paymentId = TronUSDtPaymentType.Instance.GetPaymentMethodId(network.CryptoCode);
        var handler = (TronUSDtLikePaymentMethodHandler)handlers[paymentId];

        var expandedInvoices = invoices.Select(entity => (Invoice: entity,
                Prompt: entity.GetPaymentPrompt(paymentId),
                PaymentMethodDetails: handler.ParsePaymentPromptDetails(entity.GetPaymentPrompt(paymentId)!.Details)))
            .Select(tuple => (
                tuple.Invoice,
                tuple.PaymentMethodDetails,
                tuple.Prompt
            )).ToArray();

        var invoicesPerAddress = expandedInvoices.Where(i => i.Prompt is { Destination: not null })
            .ToDictionary(i => i.Prompt!.Destination.ToLowerInvariant(), i => i);

        var web3Client = tronUSDtRpcProvider.GetWeb3Client(cryptoCode);
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
                .Equals(GetConfig(cryptoCode).SmartContractAddress, StringComparison.InvariantCultureIgnoreCase))
            .Where(t => invoicesPerAddress.ContainsKey(TronUSDtAddressHelper.HexToBase58(t.Event.To)
                .ToLowerInvariant()));

        foreach (var t in matches)
        {
            var (invoice, _, _) =
                invoicesPerAddress[TronUSDtAddressHelper.HexToBase58(t.Event.To).ToLowerInvariant()];
            await HandlePaymentData(cryptoCode, TronUSDtAddressHelper.HexToBase58(t.Event.From),
                TronUSDtAddressHelper.HexToBase58(t.Event.To),
                t.Event.Value.ToHexBigInteger().ToLong(),
                $"{t.Log.TransactionHash.Replace("0x", "")}-{t.Log.TransactionIndex}", 0, block.Number.ToLong(),
                invoice);
        }

        var updatedPaymentEntities = 
            new BlockingCollection<(PaymentEntity Payment, InvoiceEntity invoice)>();
        foreach (var invoice in invoices)
            foreach (var payment in GetPendingTronUSDtLikePayments(invoice, cryptoCode))
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

    private async Task OnNewBlockToIndex(string cryptoCode, BlockWithTransactions block)
    {
        await UpdateAnyPendingTronUSDtLikePayment(cryptoCode, block);
        eventAggregator.Publish(new NewBlockEvent { CryptoCode = cryptoCode });
    }

    private async Task HandlePaymentData(
        string cryptoCode,
        string from,
        string to,
        BigInteger totalAmount,
        string txId, int confirmations, long blockHeight, InvoiceEntity invoice)
    {
        var network = networkProvider.GetNetwork(cryptoCode);
        var pmi = TronUSDtPaymentType.Instance.GetPaymentMethodId(network.CryptoCode);
        var handler = (TronUSDtLikePaymentMethodHandler)handlers[pmi];
        TronUSDtLikePaymentData details = new()
        {
            To = to,
            From = from,
            TransactionId = txId,
            ConfirmationCount = confirmations,
            BlockHeight = blockHeight,
            Amount = totalAmount,
            CryptoCode = cryptoCode
        };

        var paymentData = new PaymentData
        {
            Status =
                details.PaymentConfirmed(invoice.SpeedPolicy) ? PaymentStatus.Settled : PaymentStatus.Processing,
            Amount = details.GetValue(),
            Created = DateTimeOffset.UtcNow,
            Id = txId,
            Currency = network.CryptoCode,
            InvoiceDataId = invoice.Id
        }.Set(invoice, handler, details);

        var payment = await paymentService.AddPayment(paymentData, [txId]);
        if (payment != null)
            await ReceivedPayment(invoice, payment);
    }


    private async Task UpdateAnyPendingTronUSDtLikePayment(string cryptoCode, BlockWithTransactions block)
    {
        var paymentMethodId = TronUSDtPaymentType.Instance.GetPaymentMethodId(cryptoCode);

        var invoices = (await invoiceRepository.GetMonitoredInvoices(paymentMethodId, true))
            .Where(i => StatusToTrack.Contains(i.Status))
            .Where(i => i.GetPaymentPrompt(paymentMethodId)?.Activated is true)
            .ToArray();
        
        if (invoices.Length == 0)
            return;

        await UpdatePaymentStates(cryptoCode, invoices, block);
    }

    private static IEnumerable<PaymentEntity> GetPendingTronUSDtLikePayments(InvoiceEntity invoice, string cryptoCode)
    {
        return invoice.GetPayments(false)
            .Where(p => p.PaymentMethodId == TronUSDtPaymentType.Instance.GetPaymentMethodId(cryptoCode))
            .Where(p => p.Status == PaymentStatus.Processing);
    }
}