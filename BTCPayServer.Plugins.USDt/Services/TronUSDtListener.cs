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
    InvoiceRepository invoiceRepository,
    ISettingsRepository settingsRepository,
    EventAggregator eventAggregator,
    TronUSDtRPCProvider tronUSDtRpcProvider,
    USDtPluginConfiguration usdtPluginConfiguration,
    USDtChainActivationService activationService,
    ILogger<TronUSDtListener> logger,
    PaymentMethodHandlerDictionary handlers,
    PaymentService paymentService)
    : USDtListener<TronUSDtLikeConfigurationItem, TronUSDtLikePaymentData>(
        invoiceRepository,
        settingsRepository,
        eventAggregator,
        tronUSDtRpcProvider,
        activationService,
        logger,
        handlers,
        paymentService)
{
    protected override IReadOnlyDictionary<PaymentMethodId, TronUSDtLikeConfigurationItem> GetConfigurations()
    {
        return usdtPluginConfiguration.TronUSDtLikeConfigurationItems;
    }

    protected override string GetListenerStateSettingKey(TronUSDtLikeConfigurationItem config)
    {
        return TronUSDtRPCProvider.ListenerStateSettingKey(config);
    }

    protected override string RateLimitNodeName => "Tron";

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
        var transferEvent = web3Client.Eth.GetEvent<TransferEventDTO>();

        List<EventLog<TransferEventDTO>>? changes;
        var tries = 0;
        do
        {
            changes = await transferEvent.GetAllChangesAsync(
                transferEvent.CreateFilterInput(new BlockParameter(block.Number), new BlockParameter(block.Number)));

            if (changes != null && changes.Count != 0)
                break;

            await Task.Delay(250, stoppingToken);
        } while (tries++ < 3);

        if (changes == null)
            throw new InvalidOperationException($"Unable to get changes {block.Number}");

        var contractAddress = usdtPluginConfiguration.TronUSDtLikeConfigurationItems[paymentMethodId].SmartContractAddress;
        return changes
            .Where(t => !t.Log.Removed && TronUSDtAddressHelper.HexToBase58(t.Log.Address)
                .Equals(contractAddress, StringComparison.InvariantCultureIgnoreCase))
            .Select(t => new USDtTransferMatch(
                TronUSDtAddressHelper.HexToBase58(t.Event.To).ToLowerInvariant(),
                TronUSDtAddressHelper.HexToBase58(t.Event.From),
                TronUSDtAddressHelper.HexToBase58(t.Event.To),
                t.Event.Value,
                $"{t.Log.TransactionHash.Replace("0x", "")}-{t.Log.TransactionIndex}"))
            .Where(match => invoicesPerAddress.ContainsKey(match.DestinationKey))
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
