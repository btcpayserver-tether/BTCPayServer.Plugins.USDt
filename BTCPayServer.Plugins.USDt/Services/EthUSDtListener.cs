using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.Ethereum;
using BTCPayServer.Plugins.USDt.Services.Events;
using BTCPayServer.Plugins.USDt.Services.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Logging;
using NBXplorer;
using Nethereum.Contracts;
using Nethereum.Contracts.Standards.ERC20.ContractDefinition;
using Nethereum.RPC.Eth.DTOs;

namespace BTCPayServer.Plugins.USDt.Services;

public class EthUSDtListener : EVMListener<EthUSDtLikeConfigurationItem, EthUSDtRPCProvider, EthUSDtPaymentMethodHandler, EthUSDtPaymentData, EthUSDtSettingsChanged>
{
    public EthUSDtListener(
        InvoiceRepository invoiceRepository,
        ISettingsRepository settingsRepository,
        EventAggregator eventAggregator,
        EthUSDtRPCProvider rpcProvider,
        USDtPluginConfiguration usdtPluginConfiguration,
        ILogger<EthUSDtListener> logger,
        PaymentMethodHandlerDictionary handlers,
        PaymentService paymentService)
        : base(invoiceRepository, settingsRepository, eventAggregator, rpcProvider,
            usdtPluginConfiguration, logger, handlers, paymentService)
    {
    }

    protected override string ChainName => "Ethereum";

    protected override IDictionary<PaymentMethodId, EthUSDtLikeConfigurationItem> GetConfigurationItems()
        => PluginConfiguration.EthereumUSDtLikeConfigurationItems;

    protected override int GetBlockPollIntervalMs() => 1_000; // Ethereum has ~12s blocks

    // Ethereum addresses are already in hex format
    protected override string AddressToHex(string address) => address;
    protected override string HexToAddress(string hex) => hex;

    protected override EthUSDtPaymentData CreatePaymentData(string from, string to, string txId, int confirmations, long blockHeight)
    {
        return new EthUSDtPaymentData
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
        var transferEvent = web3Client.Eth.GetEvent<TransferEventDTO>(config.SmartContractAddress.ToLowerInvariant());

        // Build batched topic filter on destination addresses to keep logs light
        var toAddresses = watchAddresses.ToArray();
        var changes = new List<EventLog<TransferEventDTO>>();
        var tries = 0;
        do
        {
            changes.Clear();
            foreach (var toAddress in toAddresses)
            {
                // Use overload: CreateFilterInput(firstIndexed, secondIndexed, fromBlock, toBlock)
                // ERC20 Transfer has two indexed params: from (topic1), to (topic2). We wildcard 'from' and filter on single 'to'.
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

        // Filter to watched addresses
        var watchSet = watchAddresses.Select(a => a.ToLowerInvariant()).ToHashSet();
        return changes
            .Where(t => t.Log.Removed == false)
            .Where(t => watchSet.Contains(t.Event.To.ToLowerInvariant()))
            .Select(t => (
                From: t.Event.From,
                To: t.Event.To,
                Value: t.Event.Value,
                TxHash: t.Log.TransactionHash,
                TxIndex: (int)t.Log.TransactionIndex.Value
            ));
    }

    protected override object? ParsePaymentPromptDetails(EthUSDtPaymentMethodHandler handler, Newtonsoft.Json.Linq.JToken details)
        => handler.ParsePaymentPromptDetails(details);

    protected override EthUSDtPaymentData ParsePaymentDetails(EthUSDtPaymentMethodHandler handler, Newtonsoft.Json.Linq.JToken details)
        => handler.ParsePaymentDetails(details);

    protected override void SetPaymentDetails(EthUSDtPaymentMethodHandler handler, PaymentEntity payment, EthUSDtPaymentData paymentData)
        => payment.SetDetails(handler, paymentData);
}
