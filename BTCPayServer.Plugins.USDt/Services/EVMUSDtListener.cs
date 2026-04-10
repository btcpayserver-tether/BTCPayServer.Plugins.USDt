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
    private readonly ILogger<EVMUSDtListener> _logger = logger;

    protected override IReadOnlyDictionary<PaymentMethodId, EVMUSDtLikeConfigurationItem> GetConfigurations()
    {
        return usdtPluginConfiguration.EVMUSDtLikeConfigurationItems;
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
        var web3Client = rpcProvider.GetWeb3Client(paymentMethodId);
        var transferEvent = web3Client.Eth.GetEvent<TransferEventDTO>(configuration.SmartContractAddress.ToLowerInvariant());

        var toAddresses = invoicesPerAddress.Keys.ToArray();
        List<EventLog<TransferEventDTO>> changes = [];
        var tries = 0;
        do
        {
            changes.Clear();
            const int batchSize = 1;
            for (var i = 0; i < toAddresses.Length; i += batchSize)
            {
                var toAddress = toAddresses[i];
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

        return changes
            .Where(t => !t.Log.Removed)
            .Select(t => new USDtTransferMatch(
                t.Event.To.ToLowerInvariant(),
                t.Event.From,
                t.Event.To,
                t.Event.Value,
                $"{t.Log.TransactionHash.Replace("0x", "")}-{t.Log.TransactionIndex}"))
            .Where(match => invoicesPerAddress.ContainsKey(match.DestinationKey))
            .ToArray();
    }

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