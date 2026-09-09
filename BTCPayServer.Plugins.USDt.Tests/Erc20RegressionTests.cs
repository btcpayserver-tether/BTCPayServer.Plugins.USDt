using System.Numerics;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Services;
using Microsoft.Extensions.Configuration;
using NBitcoin;
using NBXplorer;
using Xunit;

namespace BTCPayServer.Plugins.USDt.Tests;

public class Erc20RegressionTests
{
    [Fact]
    public void EthereumUsdtIsAlreadyRegisteredWithCanonicalContractAndSixDecimals()
    {
        var configs = USDtConfigurationProvider.GetEVMUSDtLikeDefaultConfigurationItems(
            new NBXplorerNetworkProvider(ChainName.Mainnet), new ConfigurationBuilder().Build());
        var eth = configs.Values.Single(c => c.ChainId == 1);
        Assert.Equal("USDT-ETHEREUM", eth.GetPaymentMethodId().ToString());
        Assert.Equal("0xdac17f958d2ee523a2206206994597c13d831ec7", eth.SmartContractAddress);
        Assert.Equal(6, eth.Divisibility);
        Assert.Contains(configs.Values, c => c.ChainId == 137);
        Assert.Contains(configs.Values, c => c.ChainId == 56);
    }

    [Fact]
    public void BatchTransfersKeepLegacyIdAndCreditEachAdditionalLogExactlyOnce()
    {
        const string destination = "0x1111111111111111111111111111111111111111";
        var first = new EVMUSDtListener.TransferLogSnapshot(destination, destination, 1_000_000, "0xabc", "3", false, 7);
        var second = first with { Value = 2_000_000, LogIndex = 8 };
        var result = EVMUSDtListener.ToTransferMatchSnapshots([second, first, first, second], [destination]);
        Assert.Equal(2, result.Count);
        Assert.Equal(new BigInteger(3_000_000), result.Aggregate(BigInteger.Zero, (sum, transfer) => sum + transfer.TotalAmount));
        Assert.Contains(result, m => m.TransactionId == "abc-3");
        Assert.Contains(result, m => m.TransactionId == "abc-3-log-8");
        Assert.Equal(result, EVMUSDtListener.ToTransferMatchSnapshots([first, second], [destination]));
    }

    [Fact]
    public void RescanKeepsPreviouslyCreditedTransferEvenWhenRpcBatchOrderChanges()
    {
        const string destination = "0x1111111111111111111111111111111111111111";
        var first = new EVMUSDtListener.TransferLogSnapshot(destination, destination, 1_000_000, "0xabc", "3", false, 7);
        var second = first with { Value = 2_000_000, LogIndex = 8 };
        var result = EVMUSDtListener.ToTransferMatchSnapshots([first, second], [destination],
            [new EVMUSDtListener.ExistingTransferSnapshot("abc-3", destination, 2_000_000)]);
        Assert.Equal(new BigInteger(2_000_000), result.Single(m => m.TransactionId == "abc-3").TotalAmount);
        Assert.Equal(new BigInteger(1_000_000), result.Single(m => m.TransactionId == "abc-3-log-7").TotalAmount);
    }

    [Fact]
    public void MismatchedHistoricalPaymentFailsClosed()
    {
        const string destination = "0x1111111111111111111111111111111111111111";
        var transfer = new EVMUSDtListener.TransferLogSnapshot(destination, destination, 1, "0xabc", "3", false, 7);
        Assert.Throws<InvalidOperationException>(() => EVMUSDtListener.ToTransferMatchSnapshots([transfer], [destination],
            [new EVMUSDtListener.ExistingTransferSnapshot("abc-3", destination, 2)]));
    }
}
