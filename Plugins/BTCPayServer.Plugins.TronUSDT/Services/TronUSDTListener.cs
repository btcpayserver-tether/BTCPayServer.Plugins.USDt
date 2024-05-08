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
using BTCPayServer.Plugins.TronUSDT.Tron.TronUSDT;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBXplorer;
using Nethereum.BlockchainProcessing.BlockStorage.Entities.Mapping;
using Nethereum.Contracts.Standards.ERC20.ContractDefinition;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;

namespace BTCPayServer.Plugins.TronUSDT.Services;

public class TronUSDTListener : IHostedService
{
    public static readonly List<InvoiceStatus> StatusToTrack =
    [
        InvoiceStatus.New,
        InvoiceStatus.Processing
    ];

    private readonly EventAggregator _eventAggregator;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly InvoiceActivator _invoiceActivator;
    private readonly InvoiceRepository _invoiceRepository;
    private readonly CompositeDisposable _leases = new();
    private readonly ILogger<TronUSDTListener> _logger;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly PaymentService _paymentService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly TronUSDTLikeConfiguration _tronUSDTLikeConfiguration;
    private readonly TronUSDTRPCProvider _tronUSDTRpcProvider;
    private CancellationTokenSource _cts;

    public TronUSDTListener(InvoiceRepository invoiceRepository,
        ISettingsRepository settingsRepository,
        EventAggregator eventAggregator,
        TronUSDTRPCProvider tronUSDTRpcProvider,
        TronUSDTLikeConfiguration tronUSDTLikeConfiguration,
        BTCPayNetworkProvider networkProvider,
        ILogger<TronUSDTListener> logger,
        PaymentMethodHandlerDictionary handlers,
        InvoiceActivator invoiceActivator,
        PaymentService paymentService)
    {
        _invoiceRepository = invoiceRepository;
        _settingsRepository = settingsRepository;
        _eventAggregator = eventAggregator;
        _tronUSDTRpcProvider = tronUSDTRpcProvider;
        _tronUSDTLikeConfiguration = tronUSDTLikeConfiguration;
        _networkProvider = networkProvider;
        _logger = logger;
        _handlers = handlers;
        _invoiceActivator = invoiceActivator;
        _paymentService = paymentService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_tronUSDTLikeConfiguration.TronUSDTLikeConfigurationItems.Any()) return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // leases.Add(_eventAggregator.Subscribe<TronUSDTEvent>(OnTronUSDTEvent));
        // leases.Add(_eventAggregator.Subscribe<TronUSDTRPCProvider.TronUSDTDaemonStateChange>(e =>
        // {
        //     if (_tronUSDTRpcProvider.IsAvailable(e.CryptoCode))
        //     {
        //         logger.LogInformation($"{e.CryptoCode} just became available");
        //         _ = UpdateAnyPendingTronUSDTLikePayment(e.CryptoCode);
        //     }
        //     else
        //     {
        //         logger.LogInformation($"{e.CryptoCode} just became unavailable");
        //     }
        // }));
        _ = LoopIndex(_tronUSDTLikeConfiguration.TronUSDTLikeConfigurationItems.Keys.Single(), _cts.Token);
        // _ = WorkThroughQueue(_Cts.Token);
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
                var web3Client = _tronUSDTRpcProvider.GetWeb3Client(cryptoCode);
                if (listenerState == null)
                {
                    _logger.LogInformation("No tracking state found, new blockchain");

                    var latestBlockNumber = await web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync(
                        BlockParameter.CreateLatest());

                    listenerState = new TronUSDTListenerState { LastBlockHeight = latestBlockNumber.Value };
                    await SetTrackingState(cryptoCode, listenerState);
                }
                else
                {
                    var latestBlockNumber = await web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync(
                        BlockParameter.CreateLatest());

                    _logger.LogInformation(
                        "Tracking state, current={CurrentBlockNumber}, latest={LatestBlockNumber}",
                        listenerState.LastBlockHeight, latestBlockNumber);
                }

                // Is it useful?
                var pendingInvoices = await _invoiceRepository.GetPendingInvoices(cancellationToken: stoppingToken);
                foreach (var pendingInvoice in pendingInvoices)
                    _eventAggregator.Publish(new InvoiceNeedUpdateEvent(pendingInvoice.Id));

                while (!stoppingToken.IsCancellationRequested)
                {
                    if ((await _invoiceRepository.GetPendingInvoices(cancellationToken: stoppingToken)).Any(i => StatusToTrack.Any(s => s == i.Status)) ==
                        false)
                    {
                        var lastBlockNumber = await web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync();
                        if (lastBlockNumber > listenerState.LastBlockHeight)
                        {
                            _eventAggregator.Publish(new NewBlockEvent { CryptoCode = cryptoCode });

                            _logger.LogInformation("New block avoid from {BlockNumber} to {NewBlockNumber}",
                                listenerState.LastBlockHeight, lastBlockNumber);
                            listenerState.LastBlockHeight = lastBlockNumber;
                            Thread.Sleep(3_000);
                        }
                        else
                        {
                            Thread.Sleep(5_000);
                        }
                    }
                    else
                    {
                        var block =
                            await web3Client.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(
                                new BlockParameter((listenerState.LastBlockHeight + 1).ToHexBigInteger()));

                        if (block != null)
                        {
                            await OnNewBlockToIndex(cryptoCode, block);
                            _logger.LogInformation("New block indexed {BlockNumber}", block.Number);

                            listenerState.LastBlockHeight = block.Number.Value;
                        }
                    }


                    if (listenerState.LastBlockHeight % 1 == 0) await SetTrackingState(cryptoCode, listenerState);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Oups");
                Thread.Sleep(10_000);
            }
        // var knownAddresses = (await LoadAddressToTrack()).Select(a => a.Value.ToLower()).ToHashSet();
        // if (knownAddresses.Any() == false)
        // {
        //     logger.LogInformation("No addresses to track, waiting");
        //     var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken,
        //         new CancellationTokenSource(30_000).Token);
        //     await newTrackedAddressChannel.WaitToReadAsync(combinedToken.Token);
        // }
    }

    private async Task SetTrackingState(string cryptoCode, TronUSDTListenerState trackingState)
    {
        await _settingsRepository.UpdateSetting(trackingState, TronUSDTRPCProvider.ListenerStateSettingKey(cryptoCode));
    }

    private async Task<TronUSDTListenerState> LoadTrackingState(string cryptoCode)
    {
        return await _settingsRepository.GetSettingAsync<TronUSDTListenerState>(
            TronUSDTRPCProvider.ListenerStateSettingKey(cryptoCode));
    }


    private async Task ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment)
    {
        _logger.LogInformation(
            $"Invoice {invoice.Id} received payment {payment.Value} {payment.Currency} {payment.Id}");

        var prompt = invoice.GetPaymentPrompt(payment.PaymentMethodId);

        if (prompt != null &&
            prompt.Activated &&
            prompt.Destination == payment.Destination &&
            prompt.Calculate().Due > 0.0m)
        {
            await _invoiceActivator.ActivateInvoicePaymentMethod(invoice.Id, payment.PaymentMethodId, true);
            invoice = await _invoiceRepository.GetInvoice(invoice.Id);
        }

        _eventAggregator.Publish(
            new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) { Payment = payment });
    }


    public TronUSDTLikeConfigurationItem GetConfig(string cryptoCode)
    {
        return _tronUSDTLikeConfiguration.TronUSDTLikeConfigurationItems[cryptoCode];
    }

    private async Task UpdatePaymentStates(string cryptoCode, InvoiceEntity[] invoices, BlockWithTransactions block)
    {
        if (!invoices.Any()) return;

        var network = _networkProvider.GetNetwork(cryptoCode);
        var paymentId = TronUSDTPaymentType.Instance.GetPaymentMethodId(network.CryptoCode);
        var handler = (TronUSDTLikePaymentMethodHandler)_handlers[paymentId];

        //get all the required data in one list (invoice, its existing payments and the current payment method details)
        var expandedInvoices = invoices.Select(entity => (Invoice: entity,
                ExistingPayments: GetAllTronUSDTLikePayments(entity, cryptoCode),
                Prompt: entity.GetPaymentPrompt(paymentId),
                PaymentMethodDetails: handler.ParsePaymentPromptDetails(entity.GetPaymentPrompt(paymentId)
                    .Details)))
            .Select(tuple => (
                tuple.Invoice,
                tuple.PaymentMethodDetails,
                tuple.Prompt,
                ExistingPayments: tuple.ExistingPayments.Select(entity =>
                    (Payment: entity, PaymentData: handler.ParsePaymentDetails(entity.Details),
                        tuple.Invoice))
            )).ToArray();

        var accountToAddressQuery = expandedInvoices.Where(i => i.Prompt.Destination != null)
            .ToDictionary(i => i.Prompt.Destination.ToLowerInvariant(), i => i);

        var web3Client = _tronUSDTRpcProvider.GetWeb3Client(cryptoCode);
        var transferEvent = web3Client.Eth.GetEvent<TransferEventDTO>();
        var changes = await transferEvent.GetAllChangesAsync(
            transferEvent.CreateFilterInput(new BlockParameter(block.Number),
                new BlockParameter(block.Number)));


        var matches = changes
            .Where(t => t.Log.Removed == false && TronUSDTAddressHelper.HexToBase58(t.Log.Address).ToLowerInvariant() ==
                GetConfig(cryptoCode).SmartContractAddress.ToLowerInvariant())
            .Where(t => accountToAddressQuery.ContainsKey(TronUSDTAddressHelper.HexToBase58(t.Event.To)
                .ToLowerInvariant()));


        foreach (var t in matches)
        {
            var expandedInvoice =
                accountToAddressQuery[TronUSDTAddressHelper.HexToBase58(t.Event.To).ToLowerInvariant()];
            await HandlePaymentData(cryptoCode, TronUSDTAddressHelper.HexToBase58(t.Event.From),
                TronUSDTAddressHelper.HexToBase58(t.Event.To),
                t.Event.Value.ToHexBigInteger().ToLong(),
                $"{t.Log.TransactionHash.Replace("0x", "")}-{t.Log.TransactionIndex}", 0, block.Number.ToLong(),
                expandedInvoice.Invoice, null);
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

        await _paymentService.UpdatePayments(updatedPaymentEntities.Select(tuple => tuple.Item1).ToList());
        foreach (var valueTuples in updatedPaymentEntities.GroupBy(entity => entity.Item2))
            if (valueTuples.Any())
                _eventAggregator.Publish(new InvoiceNeedUpdateEvent(valueTuples.Key.Id));
    }

    private async Task OnNewBlockToIndex(string cryptoCode, BlockWithTransactions block)
    {
        await UpdateAnyPendingTronUSDTLikePayment(cryptoCode, block);
        _eventAggregator.Publish(new NewBlockEvent { CryptoCode = cryptoCode });
    }

    private async Task HandlePaymentData(
        string cryptoCode,
        string address,
        string to,
        BigInteger totalAmount,
        string txId, int confirmations, long blockHeight, InvoiceEntity invoice,
        BlockingCollection<(PaymentEntity Payment, InvoiceEntity invoice)> paymentsToUpdate)
    {
        var network = _networkProvider.GetNetwork(cryptoCode);
        var pmi = TronUSDTPaymentType.Instance.GetPaymentMethodId(network.CryptoCode);
        var handler = (TronUSDTLikePaymentMethodHandler)_handlers[pmi];
        TronUSDTLikePaymentData details = new()
        {
            From = address,
            To = to,
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

        var payment = await _paymentService.AddPayment(paymentData, [txId]);
        if (payment != null)
            await ReceivedPayment(invoice, payment);
    }


    private async Task UpdateAnyPendingTronUSDTLikePayment(string cryptoCode, BlockWithTransactions block)
    {
        var invoices = (await _invoiceRepository.GetPendingInvoices()).Where(i => StatusToTrack.Contains(i.Status)).ToArray();
        if (!invoices.Any())
            return;

        var paymentMethodId = TronUSDTPaymentType.Instance.GetPaymentMethodId(cryptoCode);
        invoices = invoices.Where(entity => entity.GetPaymentPrompt(paymentMethodId)?.Activated is true).ToArray();
        await UpdatePaymentStates(cryptoCode, invoices, block);
    }

    private IEnumerable<PaymentEntity> GetAllTronUSDTLikePayments(InvoiceEntity invoice, string cryptoCode)
    {
        return invoice.GetPayments(false)
            .Where(p => p.PaymentMethodId == TronUSDTPaymentType.Instance.GetPaymentMethodId(cryptoCode));
    }
}