using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Plugins.TronUSDT.Configuration;
using BTCPayServer.Plugins.TronUSDT.Services.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBXplorer;
using Nethereum.BlockchainProcessing.BlockStorage.Entities.Mapping;
using Nethereum.Contracts.Standards.ERC20.ContractDefinition;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;

namespace BTCPayServer.Plugins.TronUSDT.Services;

public class TronUSDTListener(
    InvoiceRepository invoiceRepository,
    ISettingsRepository settingsRepository,
    EventAggregator eventAggregator,
    TronUSDTRPCProvider tronUSDTRpcProvider,
    TronUSDTLikeConfiguration tronUSDTLikeConfiguration,
    BTCPayNetworkProvider networkProvider,
    ILogger<TronUSDTListener> logger,
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
        if (tronUSDTLikeConfiguration.TronUSDTLikeConfigurationItems.Count == 0) return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = LoopIndex(tronUSDTLikeConfiguration.TronUSDTLikeConfigurationItems.Keys.Single(), _cts.Token);
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
                var web3Client = tronUSDTRpcProvider.GetWeb3Client(cryptoCode);
                if (listenerState == null)
                {
                    logger.LogInformation("No tracking state found, new blockchain");

                    var latestBlockNumber = await web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync(
                        BlockParameter.CreateLatest());

                    listenerState = new TronUSDTListenerState { LastBlockHeight = latestBlockNumber.Value };
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

                // Is it useful?
                var pendingInvoices = await invoiceRepository.GetPendingInvoices(cancellationToken: stoppingToken);
                foreach (var pendingInvoice in pendingInvoices)
                    eventAggregator.Publish(new InvoiceNeedUpdateEvent(pendingInvoice.Id));

                while (!stoppingToken.IsCancellationRequested)
                {
                    if ((await invoiceRepository.GetPendingInvoices(cancellationToken: stoppingToken)).Any(i => StatusToTrack.Any(s => s == i.Status)) ==
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
                    }


                    if (listenerState.LastBlockHeight % 1 == 0) await SetTrackingState(cryptoCode, listenerState);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Oups");
                Thread.Sleep(10_000);
            }
    }

    private async Task SetTrackingState(string cryptoCode, TronUSDTListenerState trackingState)
    {
        await settingsRepository.UpdateSetting(trackingState, TronUSDTRPCProvider.ListenerStateSettingKey(cryptoCode));
    }

    private async Task<TronUSDTListenerState?> LoadTrackingState(string cryptoCode)
    {
        return await settingsRepository.GetSettingAsync<TronUSDTListenerState>(
            TronUSDTRPCProvider.ListenerStateSettingKey(cryptoCode));
    }

    private Task ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment)
    {
        logger.LogInformation(
            $"Invoice {invoice.Id} received payment {payment.Value} {payment.Currency} {payment.Id}");

        eventAggregator.Publish(
            new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) { Payment = payment });
        return Task.CompletedTask;
    }

    private TronUSDTLikeConfigurationItem GetConfig(string cryptoCode)
    {
        return tronUSDTLikeConfiguration.TronUSDTLikeConfigurationItems[cryptoCode];
    }

    private async Task UpdatePaymentStates(string cryptoCode, InvoiceEntity[] invoices, BlockWithTransactions block)
    {
        if (invoices.Length == 0) return;

        var network = networkProvider.GetNetwork(cryptoCode);
        var paymentId = TronUSDTPaymentType.Instance.GetPaymentMethodId(network.CryptoCode);
        var handler = (TronUSDTLikePaymentMethodHandler)handlers[paymentId];

        //get all the required data in one list (invoice, its existing payments and the current payment method details)
        var expandedInvoices = invoices.Select(entity => (Invoice: entity,
                ExistingPayments: GetAllTronUSDTLikePayments(entity, cryptoCode),
                Prompt: entity.GetPaymentPrompt(paymentId),
                PaymentMethodDetails: handler.ParsePaymentPromptDetails(entity!.GetPaymentPrompt(paymentId)!
                    .Details)))
            .Select(tuple => (
                tuple.Invoice,
                tuple.PaymentMethodDetails,
                tuple.Prompt,
                ExistingPayments: tuple.ExistingPayments.Select(entity =>
                    (Payment: entity, PaymentData: handler.ParsePaymentDetails(entity.Details),
                        tuple.Invoice))
            )).ToArray();

        var accountToAddressQuery = expandedInvoices.Where(i => i.Prompt is { Destination: not null })
            .ToDictionary(i => i.Prompt!.Destination.ToLowerInvariant(), i => i);

        var web3Client = tronUSDTRpcProvider.GetWeb3Client(cryptoCode);
        var transferEvent = web3Client.Eth.GetEvent<TransferEventDTO>();
        var changes = await transferEvent.GetAllChangesAsync(
            transferEvent.CreateFilterInput(new BlockParameter(block.Number),
                new BlockParameter(block.Number)));

        var matches = changes
            .Where(t => t.Log.Removed == false && TronUSDTAddressHelper.HexToBase58(t.Log.Address)
                .Equals(GetConfig(cryptoCode).SmartContractAddress, StringComparison.InvariantCultureIgnoreCase))
            .Where(t => accountToAddressQuery.ContainsKey(TronUSDTAddressHelper.HexToBase58(t.Event.To)
                .ToLowerInvariant()));

        foreach (var t in matches)
        {
            var (invoice, _, _, _) =
                accountToAddressQuery[TronUSDTAddressHelper.HexToBase58(t.Event.To).ToLowerInvariant()];
            await HandlePaymentData(cryptoCode, TronUSDTAddressHelper.HexToBase58(t.Event.From),
                TronUSDTAddressHelper.HexToBase58(t.Event.To),
                t.Event.Value.ToHexBigInteger().ToLong(),
                $"{t.Log.TransactionHash.Replace("0x", "")}-{t.Log.TransactionIndex}", 0, block.Number.ToLong(),
                invoice);
        }

        var updatedPaymentEntities =
            new BlockingCollection<(PaymentEntity Payment, InvoiceEntity invoice)>();
        foreach (var invoice in invoices)
            foreach (var payment in GetAllTronUSDTLikePayments(invoice, cryptoCode)
                         .Where(p => p.Status == PaymentStatus.Processing))
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
        await UpdateAnyPendingTronUSDTLikePayment(cryptoCode, block);
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
        var pmi = TronUSDTPaymentType.Instance.GetPaymentMethodId(network.CryptoCode);
        var handler = (TronUSDTLikePaymentMethodHandler)handlers[pmi];
        TronUSDTLikePaymentData details = new()
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


    private async Task UpdateAnyPendingTronUSDTLikePayment(string cryptoCode, BlockWithTransactions block)
    {
        var invoices = (await invoiceRepository.GetPendingInvoices()).Where(i => StatusToTrack.Contains(i.Status)).ToArray();
        if (invoices.Length == 0)
            return;

        var paymentMethodId = TronUSDTPaymentType.Instance.GetPaymentMethodId(cryptoCode);
        invoices = invoices.Where(entity => entity.GetPaymentPrompt(paymentMethodId)?.Activated is true).ToArray();
        await UpdatePaymentStates(cryptoCode, invoices, block);
    }

    private static IEnumerable<PaymentEntity> GetAllTronUSDTLikePayments(InvoiceEntity invoice, string cryptoCode)
    {
        return invoice.GetPayments(false)
            .Where(p => p.PaymentMethodId == TronUSDTPaymentType.Instance.GetPaymentMethodId(cryptoCode));
    }
}