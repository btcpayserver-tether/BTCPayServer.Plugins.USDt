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

/// <summary>
/// Base class for EVM-based blockchain listeners (Tron, Ethereum, etc.)
/// Handles block indexing, payment detection, and confirmation tracking.
/// </summary>
/// <typeparam name="TConfig">The configuration item type</typeparam>
/// <typeparam name="TProvider">The RPC provider type</typeparam>
/// <typeparam name="THandler">The payment method handler type</typeparam>
/// <typeparam name="TPaymentData">The payment data type</typeparam>
/// <typeparam name="TSettingsChanged">The settings changed event type</typeparam>
public abstract class EVMListener<TConfig, TProvider, THandler, TPaymentData, TSettingsChanged> : IHostedService
    where TConfig : USDtPluginConfigurationItem, IEVMConfigurationItem
    where TProvider : EVMRPCProvider<TConfig, TSettingsChanged>
    where THandler : IPaymentMethodHandler
    where TPaymentData : EVMPaymentData, new()
{
    public static readonly List<InvoiceStatus> StatusToTrack =
    [
        InvoiceStatus.New,
        InvoiceStatus.Processing
    ];

    private readonly CompositeDisposable _leases = new();
    private CancellationTokenSource? _cts;

    protected readonly InvoiceRepository InvoiceRepository;
    protected readonly ISettingsRepository SettingsRepository;
    protected readonly EventAggregator EventAggregator;
    protected readonly TProvider RpcProvider;
    protected readonly USDtPluginConfiguration PluginConfiguration;
    protected readonly ILogger Logger;
    protected readonly PaymentMethodHandlerDictionary Handlers;
    protected readonly PaymentService PaymentService;

    protected EVMListener(
        InvoiceRepository invoiceRepository,
        ISettingsRepository settingsRepository,
        EventAggregator eventAggregator,
        TProvider rpcProvider,
        USDtPluginConfiguration pluginConfiguration,
        ILogger logger,
        PaymentMethodHandlerDictionary handlers,
        PaymentService paymentService)
    {
        InvoiceRepository = invoiceRepository;
        SettingsRepository = settingsRepository;
        EventAggregator = eventAggregator;
        RpcProvider = rpcProvider;
        PluginConfiguration = pluginConfiguration;
        Logger = logger;
        Handlers = handlers;
        PaymentService = paymentService;
    }

    /// <summary>
    /// Get the configuration items dictionary for this listener
    /// </summary>
    protected abstract IDictionary<PaymentMethodId, TConfig> GetConfigurationItems();

    /// <summary>
    /// Get block time in milliseconds for idle polling
    /// </summary>
    protected abstract int GetBlockPollIntervalMs();

    /// <summary>
    /// Convert address from chain-specific format to hex for RPC calls
    /// </summary>
    protected abstract string AddressToHex(string address);

    /// <summary>
    /// Convert address from hex to chain-specific format
    /// </summary>
    protected abstract string HexToAddress(string hex);

    /// <summary>
    /// Get the chain name for logging (e.g., "Tron", "Ethereum")
    /// </summary>
    protected abstract string ChainName { get; }

    /// <summary>
    /// Create a new payment data instance
    /// </summary>
    protected abstract TPaymentData CreatePaymentData(string from, string to, string txId, int confirmations, long blockHeight);

    /// <summary>
    /// Get transfers from a block. Different chains may have different ways to query transfers.
    /// </summary>
    protected abstract Task<IEnumerable<(string From, string To, BigInteger Value, string TxHash, int TxIndex)>> GetTransfersFromBlock(
        PaymentMethodId pmi, BlockWithTransactions block, IReadOnlyCollection<string> watchAddresses, CancellationToken stoppingToken);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var configItems = GetConfigurationItems();
        if (configItems.Count == 0) return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        foreach (var kv in configItems)
            _ = LoopIndex(kv.Value, _cts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _leases.Dispose();
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task LoopIndex(TConfig configurationItem, CancellationToken stoppingToken)
    {
        var rateLimitBackoffMs = 5_000;
        while (stoppingToken.IsCancellationRequested == false)
            try
            {
                var listenerState = await LoadTrackingState(configurationItem);
                var pmi = configurationItem.GetPaymentMethodId();

                var web3Client = RpcProvider.GetWeb3Client(pmi);
                if (listenerState == null)
                {
                    Logger.LogInformation("No tracking state found, new blockchain");

                    var latestBlockNumber = await web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync(
                        BlockParameter.CreateLatest());

                    listenerState = new EVMBasedListenerState { LastBlockHeight = (long)latestBlockNumber.Value - 1 };
                    await SetTrackingState(configurationItem, listenerState);
                }
                else
                {
                    var latestBlockNumber = await web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync(
                        BlockParameter.CreateLatest());

                    Logger.LogInformation(
                        "Tracking state, current={CurrentBlockNumber}, latest={LatestBlockNumber}",
                        listenerState.LastBlockHeight, latestBlockNumber);
                }

                while (!stoppingToken.IsCancellationRequested)
                {
                    if ((await InvoiceRepository.GetMonitoredInvoices(pmi, true, stoppingToken)).Any(i =>
                            StatusToTrack.Any(s => s == i.Status)) ==
                        false)
                    {
                        var lastBlockNumber = await web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync();
                        if (lastBlockNumber.Value > listenerState.LastBlockHeight)
                        {
                            Logger.LogDebug("New block avoid from {BlockNumber} to {NewBlockNumber}",
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
                            Logger.LogInformation("New block indexed {BlockNumber}", block.Number);

                            listenerState.LastBlockHeight = (long)block.Number.Value;
                        }
                        else
                        {
                            Logger.LogInformation("Block not present on node yet {BlockNumber}",
                                listenerState.LastBlockHeight);
                            await Task.Delay(GetBlockPollIntervalMs(), stoppingToken);
                        }
                    }

                    await SetTrackingState(configurationItem, listenerState);
                    rateLimitBackoffMs = 5_000; // reset after successful iteration
                }
            }
            catch (RpcClientTimeoutException)
            {
                Logger.LogWarning("Timeout while indexing, is the node running? Retrying in 10 seconds");
                await Task.Delay(5_000, stoppingToken);
            }
            catch (RpcClientUnknownException e) when (e.InnerException?.Message?.Contains("429 (Too Many Requests)") ==
                                                      true)
            {
                Logger.LogWarning(
                    "Rate limit exceeded while indexing, use a {ChainName} node with higher limits if possible. Retrying in {DelayMs} ms",
                    ChainName, rateLimitBackoffMs);
                await Task.Delay(rateLimitBackoffMs, stoppingToken);
                rateLimitBackoffMs = Math.Min(rateLimitBackoffMs * 2, 60_000);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "An error occurred while indexing");
                await Task.Delay(10_000, stoppingToken);
            }
    }

    private async Task SetTrackingState(TConfig config, EVMBasedListenerState trackingState)
    {
        await SettingsRepository.UpdateSetting(trackingState, EVMRPCProvider<TConfig, TSettingsChanged>.ListenerStateSettingKey(config));
    }

    private async Task<EVMBasedListenerState?> LoadTrackingState(TConfig config)
    {
        return await SettingsRepository.GetSettingAsync<EVMBasedListenerState>(
            EVMRPCProvider<TConfig, TSettingsChanged>.ListenerStateSettingKey(config));
    }

    private Task ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment)
    {
        Logger.LogInformation(
            $"Invoice {invoice.Id} received payment {payment.Value} {payment.Currency} {payment.Id}");

        EventAggregator.Publish(
            new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) { Payment = payment });
        return Task.CompletedTask;
    }

    protected TConfig GetConfig(PaymentMethodId pmi)
    {
        return GetConfigurationItems()[pmi];
    }

    private async Task UpdatePaymentStates(PaymentMethodId pmi, InvoiceEntity[] invoices, BlockWithTransactions block,
        CancellationToken stoppingToken)
    {
        if (invoices.Length == 0) return;

        var handler = (THandler)Handlers[pmi];

        var expandedInvoices = invoices.Select(entity => (Invoice: entity,
                Prompt: entity.GetPaymentPrompt(pmi),
                PaymentMethodDetails: ParsePaymentPromptDetails(handler, entity.GetPaymentPrompt(pmi)!.Details)))
            .Select(tuple => (
                tuple.Invoice,
                tuple.PaymentMethodDetails,
                tuple.Prompt
            )).ToArray();

        var invoicesPerAddress = expandedInvoices.Where(i => i.Prompt is { Destination: not null })
            .ToDictionary(i => i.Prompt!.Destination!.ToLowerInvariant(), i => i);

        var toAddresses = invoicesPerAddress.Keys.ToArray();
        var transfers = await GetTransfersFromBlock(pmi, block, toAddresses, stoppingToken);

        foreach (var t in transfers)
        {
            var normalizedTo = t.To.ToLowerInvariant();
            if (!invoicesPerAddress.TryGetValue(normalizedTo, out var invoiceData))
                continue;

            var invoice = invoiceData.Invoice;
            await HandlePaymentData(pmi, t.From, t.To, t.Value,
                $"{t.TxHash.Replace("0x", "")}-{t.TxIndex}", 0, block.Number.ToLong(), invoice);
        }

        var updatedPaymentEntities = new List<(PaymentEntity Payment, InvoiceEntity invoice)>();
        foreach (var invoice in invoices)
        foreach (var payment in GetPendingPayments(invoice, pmi))
        {
            var paymentData = ParsePaymentDetails(handler, payment.Details);
            var confDiff = block.Number.Value - paymentData.BlockHeight;
            var confs = confDiff < 0 ? 0 : (long)confDiff;
            paymentData.ConfirmationCount = confs > int.MaxValue ? int.MaxValue : (int)confs;

            payment.Status = paymentData.PaymentConfirmed(invoice.SpeedPolicy)
                ? PaymentStatus.Settled
                : PaymentStatus.Processing;
            SetPaymentDetails(handler, payment, paymentData);

            updatedPaymentEntities.Add((payment, invoice));
        }

        await PaymentService.UpdatePayments(updatedPaymentEntities.Select(tuple => tuple.Payment).ToList());
        foreach (var valueTuples in updatedPaymentEntities.GroupBy(entity => entity.invoice))
            if (valueTuples.Any())
                EventAggregator.Publish(new InvoiceNeedUpdateEvent(valueTuples.Key.Id));
    }

    /// <summary>
    /// Parse payment prompt details from handler
    /// </summary>
    protected abstract object? ParsePaymentPromptDetails(THandler handler, Newtonsoft.Json.Linq.JToken details);

    /// <summary>
    /// Parse payment details from handler
    /// </summary>
    protected abstract TPaymentData ParsePaymentDetails(THandler handler, Newtonsoft.Json.Linq.JToken details);

    /// <summary>
    /// Set payment details on handler
    /// </summary>
    protected abstract void SetPaymentDetails(THandler handler, PaymentEntity payment, TPaymentData paymentData);

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
        var config = GetConfig(pmi);
        var totalAmountBigDecimal = decimal.Parse(
            Web3.Convert.FromWeiToBigDecimal(totalAmount, config.Divisibility).ToString(),
            CultureInfo.InvariantCulture);

        var handler = (THandler)Handlers[pmi];
        var details = CreatePaymentData(from, to, txId, confirmations, blockHeight);

        var paymentData = new PaymentData
        {
            Status = details.PaymentConfirmed(invoice.SpeedPolicy) ? PaymentStatus.Settled : PaymentStatus.Processing,
            Amount = totalAmountBigDecimal,
            Created = DateTimeOffset.UtcNow,
            Id = txId,
            Currency = config.Currency,
            InvoiceDataId = invoice.Id
        }.Set(invoice, handler, details);

        var payment = await PaymentService.AddPayment(paymentData, [txId]);
        if (payment != null)
            await ReceivedPayment(invoice, payment);
    }

    private async Task UpdateAnyPendingPayment(PaymentMethodId pmi, BlockWithTransactions block,
        CancellationToken stoppingToken)
    {
        var invoices = (await InvoiceRepository.GetMonitoredInvoices(pmi, true))
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
