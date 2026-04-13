using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Services.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;

namespace BTCPayServer.Plugins.USDt.Services;

public abstract class USDtListener<TConfigurationItem, TPaymentData>(
    InvoiceRepository invoiceRepository,
    ISettingsRepository settingsRepository,
    EventAggregator eventAggregator,
    USDtRPCProvider<TConfigurationItem> rpcProvider,
    ILogger logger,
    PaymentMethodHandlerDictionary handlers,
    PaymentService paymentService) : IHostedService
    where TConfigurationItem : USDtPluginConfigurationItem, IUSDtRpcConfigurationItem
    where TPaymentData : USDtPaymentData
{
    private CancellationTokenSource? _cts;

    protected abstract IReadOnlyDictionary<PaymentMethodId, TConfigurationItem> GetConfigurations();
    protected abstract string GetListenerStateSettingKey(TConfigurationItem config);
    protected abstract Task<IReadOnlyCollection<USDtTransferMatch>> GetTransfersAsync(
        PaymentMethodId paymentMethodId,
        BlockWithTransactions block,
        IReadOnlyDictionary<string, InvoiceEntity> invoicesPerAddress,
        CancellationToken stoppingToken);
    protected abstract TPaymentData CreatePaymentDetails(
        string from,
        string to,
        string transactionId,
        int confirmations,
        long blockHeight);

    protected abstract string RateLimitNodeName { get; }

    protected virtual bool UseExponentialRateLimitBackoff => false;
    protected virtual long CreateInitialLastBlockHeight(HexBigInteger latestBlockNumber) => (long)latestBlockNumber.Value - 1;
    protected virtual LogLevel EmptyQueueBlockAdvanceLogLevel => LogLevel.Information;
    protected virtual IDisposable? BeginLoggingScope(PaymentMethodId paymentMethodId) => null;
    protected virtual string NormalizeDestinationKey(string destination) => destination.ToLowerInvariant();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var configurations = GetConfigurations();
        if (configurations.Count == 0)
            return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        foreach (var configurationItem in configurations.Values)
            _ = LoopIndex(configurationItem, _cts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task LoopIndex(TConfigurationItem configurationItem, CancellationToken stoppingToken)
    {
        var rateLimitBackoffMs = 5_000;
        while (!stoppingToken.IsCancellationRequested)
            try
            {
                var listenerState = await LoadTrackingState(configurationItem);
                var paymentMethodId = configurationItem.GetPaymentMethodId();
                using var _ = BeginLoggingScope(paymentMethodId);

                var web3Client = rpcProvider.GetWeb3Client(paymentMethodId);
                if (listenerState == null)
                {
                    logger.LogInformation("No tracking state found, new blockchain");

                    var latestBlockNumber = await web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync(
                        BlockParameter.CreateLatest());

                    listenerState = new EVMBasedListenerState
                    {
                        LastBlockHeight = CreateInitialLastBlockHeight(latestBlockNumber)
                    };
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
                    if (!await HasTrackedInvoices(paymentMethodId, stoppingToken))
                    {
                        var lastBlockNumber = await web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync();
                        if (lastBlockNumber.Value > listenerState.LastBlockHeight)
                        {
                            logger.Log(EmptyQueueBlockAdvanceLogLevel,
                                "New block avoid from {BlockNumber} to {NewBlockNumber}",
                                listenerState.LastBlockHeight, lastBlockNumber.Value);
                            listenerState.LastBlockHeight = (long)lastBlockNumber.Value;
                        }

                        await Task.Delay(30_000, stoppingToken);
                    }
                    else
                    {
                        var block =
                            await web3Client.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(
                                new BlockParameter(new HexBigInteger(listenerState.LastBlockHeight + 1)));

                        if (block != null)
                        {
                            await OnNewBlockToIndex(paymentMethodId, block, stoppingToken);
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
                    rateLimitBackoffMs = 5_000;
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
                if (UseExponentialRateLimitBackoff)
                {
                    logger.LogWarning(
                        "Rate limit exceeded while indexing, use a {NodeName} node with higher limits if possible. Retrying in {DelayMs} ms",
                        RateLimitNodeName, rateLimitBackoffMs);
                    await Task.Delay(rateLimitBackoffMs, stoppingToken);
                    rateLimitBackoffMs = Math.Min(rateLimitBackoffMs * 2, 60_000);
                }
                else
                {
                    logger.LogWarning(
                        "Rate limit exceeded while indexing, use a {NodeName} node with higher limits if possible. Retrying in 10 seconds",
                        RateLimitNodeName);
                    await Task.Delay(10_000, stoppingToken);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "An error occurred while indexing");
                await Task.Delay(10_000, stoppingToken);
            }
    }

    private async Task SetTrackingState(TConfigurationItem config, EVMBasedListenerState trackingState)
    {
        await settingsRepository.UpdateSetting(trackingState, GetListenerStateSettingKey(config));
    }

    private async Task<EVMBasedListenerState?> LoadTrackingState(TConfigurationItem config)
    {
        return await settingsRepository.GetSettingAsync<EVMBasedListenerState>(GetListenerStateSettingKey(config));
    }

    private async Task<bool> HasTrackedInvoices(PaymentMethodId paymentMethodId, CancellationToken stoppingToken)
    {
        return (await invoiceRepository.GetMonitoredInvoices(paymentMethodId, true, stoppingToken))
            .Any(i => USDtListenerShared.StatusToTrack.Contains(i.Status));
    }

    private Task ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment)
    {
        logger.LogInformation(
            "Invoice {InvoiceId} received payment {PaymentValue} {PaymentCurrency} {PaymentId}",
            invoice.Id, payment.Value, payment.Currency, payment.Id);

        eventAggregator.Publish(
            new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) { Payment = payment });
        return Task.CompletedTask;
    }

    private TConfigurationItem GetConfig(PaymentMethodId paymentMethodId)
    {
        return GetConfigurations()[paymentMethodId];
    }

    private async Task UpdatePaymentStates(
        PaymentMethodId paymentMethodId,
        InvoiceEntity[] invoices,
        BlockWithTransactions block,
        CancellationToken stoppingToken)
    {
        if (invoices.Length == 0)
            return;

        var invoicesPerAddress = invoices
            .Select(invoice => (Invoice: invoice, Destination: invoice.GetPaymentPrompt(paymentMethodId)?.Destination))
            .Where(tuple => !string.IsNullOrEmpty(tuple.Destination))
            .GroupBy(tuple => NormalizeDestinationKey(tuple.Destination!))
            .ToDictionary(group => group.Key, group => group.First().Invoice);

        var matches = await GetTransfersAsync(paymentMethodId, block, invoicesPerAddress, stoppingToken);
        foreach (var match in matches)
        {
            if (!invoicesPerAddress.TryGetValue(match.DestinationKey, out var invoice))
                continue;

            await HandlePaymentData(
                paymentMethodId,
                match.From,
                match.To,
                match.TotalAmount,
                match.TransactionId,
                0,
                (long)block.Number.Value,
                invoice);
        }

        await UpdatePendingConfirmations(paymentMethodId, invoices, block);
    }

    private async Task UpdatePendingConfirmations(
        PaymentMethodId paymentMethodId,
        InvoiceEntity[] invoices,
        BlockWithTransactions block)
    {
        var handler = handlers[paymentMethodId];
        List<(PaymentEntity Payment, InvoiceEntity Invoice)> updatedPaymentEntities = [];

        foreach (var invoice in invoices)
        foreach (var payment in GetPendingPayments(invoice, paymentMethodId))
        {
            var paymentData = (USDtPaymentData)handler.ParsePaymentDetails(payment.Details);
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
        foreach (var group in updatedPaymentEntities.GroupBy(entity => entity.Invoice))
            if (group.Any())
                eventAggregator.Publish(new InvoiceNeedUpdateEvent(group.Key.Id));
    }

    private async Task OnNewBlockToIndex(
        PaymentMethodId paymentMethodId,
        BlockWithTransactions block,
        CancellationToken stoppingToken)
    {
        await UpdateAnyPendingPayment(paymentMethodId, block, stoppingToken);
    }

    private async Task HandlePaymentData(
        PaymentMethodId paymentMethodId,
        string from,
        string to,
        BigInteger totalAmount,
        string txId,
        int confirmations,
        long blockHeight,
        InvoiceEntity invoice)
    {
        var config = GetConfig(paymentMethodId);
        var handler = handlers[paymentMethodId];
        var totalAmountBigDecimal = decimal.Parse(
            Web3.Convert.FromWeiToBigDecimal(totalAmount, config.Divisibility).ToString(),
            CultureInfo.InvariantCulture);

        var details = CreatePaymentDetails(from, to, txId, confirmations, blockHeight);

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

    private async Task UpdateAnyPendingPayment(
        PaymentMethodId paymentMethodId,
        BlockWithTransactions block,
        CancellationToken stoppingToken)
    {
        var invoices = (await invoiceRepository.GetMonitoredInvoices(paymentMethodId, true))
            .Where(i => USDtListenerShared.StatusToTrack.Contains(i.Status))
            .Where(i => i.GetPaymentPrompt(paymentMethodId)?.Activated is true)
            .ToArray();

        if (invoices.Length == 0)
            return;

        await UpdatePaymentStates(paymentMethodId, invoices, block, stoppingToken);
    }

    private static IEnumerable<PaymentEntity> GetPendingPayments(InvoiceEntity invoice, PaymentMethodId paymentMethodId)
    {
        return invoice.GetPayments(false)
            .Where(p => p.PaymentMethodId == paymentMethodId)
            .Where(p => p.Status == PaymentStatus.Processing);
    }

    protected sealed record USDtTransferMatch(
        string DestinationKey,
        string From,
        string To,
        BigInteger TotalAmount,
        string TransactionId);
}