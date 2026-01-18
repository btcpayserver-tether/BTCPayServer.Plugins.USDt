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
using BTCPayServer.Plugins.USDt.Services.Events;
using BTCPayServer.Plugins.USDt.Services.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Logging;
using NBXplorer;
using Nethereum.Contracts;
using Nethereum.Contracts.Standards.ERC20.ContractDefinition;
using Nethereum.RPC.Eth.DTOs;

namespace BTCPayServer.Plugins.USDt.Services;

public class TronUSDtListener : EVMListener<TronUSDtLikeConfigurationItem, TronUSDtRPCProvider, TronUSDtLikePaymentMethodHandler, TronUSDtLikePaymentData, TronUSDtSettingsChanged>
{
    public TronUSDtListener(
        InvoiceRepository invoiceRepository,
        ISettingsRepository settingsRepository,
        EventAggregator eventAggregator,
        TronUSDtRPCProvider tronUSDtRpcProvider,
        USDtPluginConfiguration usdtPluginConfiguration,
        ILogger<TronUSDtListener> logger,
        PaymentMethodHandlerDictionary handlers,
        PaymentService paymentService)
        : base(invoiceRepository, settingsRepository, eventAggregator, tronUSDtRpcProvider,
            usdtPluginConfiguration, logger, handlers, paymentService)
    {
    }

    protected override string ChainName => "Tron";

    protected override IDictionary<PaymentMethodId, TronUSDtLikeConfigurationItem> GetConfigurationItems()
        => PluginConfiguration.TronUSDtLikeConfigurationItems;

    protected override int GetBlockPollIntervalMs() => 1_000; // Tron has ~3s blocks

    protected override string AddressToHex(string address)
        => TronUSDtAddressHelper.Base58ToHex(address);

    protected override string HexToAddress(string hex)
        => TronUSDtAddressHelper.HexToBase58(hex);

    protected override TronUSDtLikePaymentData CreatePaymentData(string from, string to, string txId, int confirmations, long blockHeight)
    {
        return new TronUSDtLikePaymentData
        {
            From = from,
            To = to,
            TransactionId = txId,
            ConfirmationCount = confirmations,
            BlockHeight = blockHeight
        };
    }

    protected override async Task<IEnumerable<(string From, string To, BigInteger Value, string TxHash, int TxIndex)>> GetTransfersFromBlock(
        PaymentMethodId pmi, BlockWithTransactions block, IReadOnlyCollection<string> watchAddresses, CancellationToken stoppingToken)
    {
        var config = GetConfig(pmi);
        var web3Client = RpcProvider.GetWeb3Client(pmi);
        var transferEvent = web3Client.Eth.GetEvent<TransferEventDTO>();

        // This is a workaround for the fact that sometimes the event is not indexed yet
        List<EventLog<TransferEventDTO>>? changes = null;
        var tries = 0;
        do
        {
            changes = await transferEvent.GetAllChangesAsync(
                transferEvent.CreateFilterInput(new BlockParameter(block.Number),
                    new BlockParameter(block.Number)));

            if (changes != null && changes.Count != 0)
                break;

            await Task.Delay(250, stoppingToken);
        } while (tries++ < 3);

        if (changes == null)
            throw new InvalidOperationException($"Unable to get changes {block.Number}");

        // Filter to our contract and watched addresses, converting Tron hex to base58
        var watchSet = watchAddresses.Select(a => a.ToLowerInvariant()).ToHashSet();
        return changes
            .Where(t => t.Log.Removed == false &&
                        TronUSDtAddressHelper.HexToBase58(t.Log.Address)
                            .Equals(config.SmartContractAddress, StringComparison.InvariantCultureIgnoreCase))
            .Where(t => watchSet.Contains(TronUSDtAddressHelper.HexToBase58(t.Event.To).ToLowerInvariant()))
            .Select(t => (
                From: TronUSDtAddressHelper.HexToBase58(t.Event.From),
                To: TronUSDtAddressHelper.HexToBase58(t.Event.To),
                Value: t.Event.Value,
                TxHash: t.Log.TransactionHash,
                TxIndex: (int)t.Log.TransactionIndex.Value
            ));
    }

    protected override object? ParsePaymentPromptDetails(TronUSDtLikePaymentMethodHandler handler, Newtonsoft.Json.Linq.JToken details)
        => handler.ParsePaymentPromptDetails(details);

    protected override TronUSDtLikePaymentData ParsePaymentDetails(TronUSDtLikePaymentMethodHandler handler, Newtonsoft.Json.Linq.JToken details)
        => handler.ParsePaymentDetails(details);

    protected override void SetPaymentDetails(TronUSDtLikePaymentMethodHandler handler, PaymentEntity payment, TronUSDtLikePaymentData paymentData)
        => payment.SetDetails(handler, paymentData);
}
