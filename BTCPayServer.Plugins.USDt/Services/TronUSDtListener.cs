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
using BTCPayServer.Plugins.USDt.Configuration.Tron;
using BTCPayServer.Plugins.USDt.Services.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Logging;
using Nethereum.Contracts;
using Nethereum.Contracts.Standards.ERC20.ContractDefinition;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;

namespace BTCPayServer.Plugins.USDt.Services;

public class TronUSDtListener(
    ISettingsRepository settingsRepository,
    EventAggregator eventAggregator,
    TronUSDtRPCProvider tronUSDtRpcProvider,
    USDtPluginConfiguration usdtPluginConfiguration,
    USDtChainActivationService activationService,
    USDtTrackedInvoiceProvider trackedInvoiceProvider,
    ILogger<TronUSDtListener> logger,
    PaymentMethodHandlerDictionary handlers,
    PaymentService paymentService)
    : USDtListener<TronUSDtLikeConfigurationItem, TronUSDtLikePaymentData>(
        settingsRepository,
        eventAggregator,
        tronUSDtRpcProvider,
        activationService,
        trackedInvoiceProvider,
        logger,
        handlers,
        paymentService)
{
    private readonly USDtTrackedInvoiceProvider _trackedInvoiceProvider = trackedInvoiceProvider;
    private readonly PaymentMethodHandlerDictionary _handlers = handlers;

    protected override IReadOnlyDictionary<PaymentMethodId, TronUSDtLikeConfigurationItem> GetConfigurations()
    {
        return usdtPluginConfiguration.TronUSDtLikeConfigurationItems;
    }

    protected override string GetListenerStateSettingKey(TronUSDtLikeConfigurationItem config)
    {
        return TronUSDtRPCProvider.ListenerStateSettingKey(config);
    }

    protected override string RateLimitNodeName => "Tron";

    protected override bool UseExponentialRateLimitBackoff => true;

    protected override TimeSpan GetHeadPollingDelay(TronUSDtLikeConfigurationItem configurationItem)
    {
        return USDtListenerShared.GetBlockPollingDelay(configurationItem.BlockTimeSeconds);
    }

    protected override long GetHeadLagBlocks(TronUSDtLikeConfigurationItem configurationItem)
    {
        return 1;
    }

    protected override long CreateInitialLastBlockHeight(HexBigInteger latestBlockNumber)
    {
        return (long)latestBlockNumber.Value;
    }

    protected override LogLevel EmptyQueueBlockAdvanceLogLevel => LogLevel.Debug;

    protected override async Task<IReadOnlyCollection<USDtTransferMatch>> GetTransfersAsync(
        PaymentMethodId paymentMethodId,
        BlockWithTransactions block,
        IReadOnlyDictionary<string, InvoiceEntity> invoicesPerAddress,
        CancellationToken stoppingToken)
    {
        var web3Client = tronUSDtRpcProvider.GetWeb3Client(paymentMethodId);
        var configuration = usdtPluginConfiguration.TronUSDtLikeConfigurationItems[paymentMethodId];
        var contractAddress = configuration.SmartContractAddress;
        var transferEvent = web3Client.Eth.GetEvent<TransferEventDTO>(
            TronUSDtAddressHelper.Base58ToHex(contractAddress));
        var changes = await transferEvent.GetAllChangesAsync(
            transferEvent.CreateFilterInput(new BlockParameter(block.Number), new BlockParameter(block.Number)));

        if (changes == null)
            throw new InvalidOperationException($"Unable to get changes {block.Number}");

        var matchedChanges = changes
            .Where(t => !t.Log.Removed && TronUSDtAddressHelper.HexToBase58(t.Log.Address)
                .Equals(contractAddress, StringComparison.Ordinal))
            .Where(t => invoicesPerAddress.ContainsKey(TronUSDtAddressHelper.HexToBase58(t.Event.To).ToLowerInvariant()))
            .Select(t => new USDtTransferMatcher.TransferLogSnapshot(
                t.Event.To,
                t.Event.From,
                t.Event.Value,
                t.Log.TransactionHash,
                t.Log.TransactionIndex?.ToString() ?? string.Empty,
                t.Log.Removed,
                t.Log.LogIndex?.Value))
            .ToArray();
        if (matchedChanges.Length == 0)
            return [];

        var invoiceIds = matchedChanges
            .Select(change => invoicesPerAddress[TronUSDtAddressHelper.HexToBase58(change.To).ToLowerInvariant()].Id)
            .Distinct()
            .ToArray();
        var invoicesWithPayments = await _trackedInvoiceProvider.GetInvoicesWithPayments(invoiceIds, stoppingToken);

        return USDtTransferMatcher.ToTransferMatchSnapshots(
                matchedChanges,
                matchedChanges.Select(change => change.To),
                invoicesWithPayments.SelectMany(invoice => invoice.GetPayments(false))
                    .Where(payment => payment.PaymentMethodId == paymentMethodId)
                    .Select(payment =>
                    {
                        var details = (TronUSDtLikePaymentData)_handlers[paymentMethodId].ParsePaymentDetails(payment.Details);
                        return new USDtTransferMatcher.ExistingTransferSnapshot(details.TransactionId,
                            TronUSDtAddressHelper.Base58ToHex(details.To),
                            Nethereum.Web3.Web3.Convert.ToWei(payment.Value, configuration.Divisibility));
                    }))
            .Select(match => new USDtTransferMatch(
                TronUSDtAddressHelper.HexToBase58(match.To).ToLowerInvariant(),
                TronUSDtAddressHelper.HexToBase58(match.From),
                TronUSDtAddressHelper.HexToBase58(match.To),
                match.TotalAmount,
                match.TransactionId))
            .ToArray();
    }

    protected override TronUSDtLikePaymentData CreatePaymentDetails(
        string from,
        string to,
        string transactionId,
        int confirmations,
        long blockHeight)
    {
        return new TronUSDtLikePaymentData
        {
            To = to,
            From = from,
            TransactionId = transactionId,
            ConfirmationCount = confirmations,
            BlockHeight = blockHeight
        };
    }
}
