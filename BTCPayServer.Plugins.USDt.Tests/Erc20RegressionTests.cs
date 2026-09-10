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
    public void BatchTransfersCreditEachLogExactlyOnceWithStableIds()
    {
        const string destination = "0x1111111111111111111111111111111111111111";
        var first = new USDtTransferMatcher.TransferLogSnapshot(destination, destination, 1_000_000, "0xabc", "3", false, 7);
        var second = first with { Value = 2_000_000, LogIndex = 8 };
        var result = USDtTransferMatcher.ToTransferMatchSnapshots([second, first, first, second], [destination]);
        Assert.Equal(2, result.Count);
        Assert.Equal(new BigInteger(3_000_000), result.Aggregate(BigInteger.Zero, (sum, transfer) => sum + transfer.TotalAmount));
        Assert.Contains(result, m => m.TransactionId == "abc-3-log-7");
        Assert.Contains(result, m => m.TransactionId == "abc-3-log-8");
        Assert.Equal(result, USDtTransferMatcher.ToTransferMatchSnapshots([first, second], [destination]));
    }

    [Fact]
    public void RescanKeepsPreviouslyCreditedTransferEvenWhenRpcBatchOrderChanges()
    {
        const string destination = "0x1111111111111111111111111111111111111111";
        var first = new USDtTransferMatcher.TransferLogSnapshot(destination, destination, 1_000_000, "0xabc", "3", false, 7);
        var second = first with { Value = 2_000_000, LogIndex = 8 };
        var result = USDtTransferMatcher.ToTransferMatchSnapshots([first, second], [destination],
            [new USDtTransferMatcher.ExistingTransferSnapshot("abc-3", destination, 2_000_000)]);
        Assert.Equal(new BigInteger(2_000_000), result.Single(m => m.TransactionId == "abc-3").TotalAmount);
        Assert.Equal(new BigInteger(1_000_000), result.Single(m => m.TransactionId == "abc-3-log-7").TotalAmount);
    }

    [Fact]
    public void MismatchedHistoricalPaymentFailsClosed()
    {
        const string destination = "0x1111111111111111111111111111111111111111";
        var transfer = new USDtTransferMatcher.TransferLogSnapshot(destination, destination, 1, "0xabc", "3", false, 7);
        Assert.Throws<InvalidOperationException>(() => USDtTransferMatcher.ToTransferMatchSnapshots([transfer], [destination],
            [new USDtTransferMatcher.ExistingTransferSnapshot("abc-3", destination, 2)]));
    }

    [Fact]
    public void RemovingTrackedDestinationDoesNotRenameRemainingTransfer()
    {
        const string firstDestination = "0x1111111111111111111111111111111111111111";
        const string secondDestination = "0x2222222222222222222222222222222222222222";
        var first = new USDtTransferMatcher.TransferLogSnapshot(firstDestination, firstDestination, 1_000_000, "0xabc", "3", false, 7);
        var second = first with { To = secondDestination, LogIndex = 8 };
        var initial = USDtTransferMatcher.ToTransferMatchSnapshots([first, second], [firstDestination, secondDestination]);
        var remaining = initial.Single(match => match.To == secondDestination);
        var replay = USDtTransferMatcher.ToTransferMatchSnapshots([second], [secondDestination],
            [new USDtTransferMatcher.ExistingTransferSnapshot(remaining.TransactionId, remaining.To, remaining.TotalAmount)]);
        Assert.Equal(remaining, Assert.Single(replay));
    }

    [Fact]
    public void AddingTrackedDestinationDoesNotRenamePreviouslySeenTransfer()
    {
        const string destination = "0x1111111111111111111111111111111111111111";
        var first = new USDtTransferMatcher.TransferLogSnapshot(destination, destination, 1_000_000, "0xabc", "3", false, 7);
        var second = first with { To = "0x2222222222222222222222222222222222222222", LogIndex = 8 };
        var initial = Assert.Single(USDtTransferMatcher.ToTransferMatchSnapshots([second], [second.To]));
        var replay = USDtTransferMatcher.ToTransferMatchSnapshots([first, second], [first.To, second.To],
            [new USDtTransferMatcher.ExistingTransferSnapshot(initial.TransactionId, initial.To, initial.TotalAmount)]);
        Assert.Equal(initial, replay.Single(match => match.To == second.To));
    }

    [Fact]
    public void EqualAmountLegacyReplayDoesNotStealAnAlreadyIndexedLog()
    {
        const string destination = "0x1111111111111111111111111111111111111111";
        var first = new USDtTransferMatcher.TransferLogSnapshot(destination, destination, 1_000_000, "0xabc", "3", false, 7);
        var second = first with { LogIndex = 8 };
        var result = USDtTransferMatcher.ToTransferMatchSnapshots([first, second], [destination],
            [new USDtTransferMatcher.ExistingTransferSnapshot("abc-3", destination, 1_000_000),
             new USDtTransferMatcher.ExistingTransferSnapshot("abc-3-log-7", destination, 1_000_000)]);
        Assert.Equal(new[] { "abc-3-log-7", "abc-3" }, result.Select(match => match.TransactionId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    public void MissingOrNegativeLogIndexFailsClosed(int? index)
    {
        const string destination = "0x1111111111111111111111111111111111111111";
        var log = new USDtTransferMatcher.TransferLogSnapshot(destination, destination, 1, "0xabc", "3", false,
            index.HasValue ? new BigInteger(index.Value) : null);
        Assert.Throws<InvalidOperationException>(() => USDtTransferMatcher.ToTransferMatchSnapshots([log], [destination]));
    }

    [Fact]
    public void ConflictingDuplicateLogFailsClosed()
    {
        const string destination = "0x1111111111111111111111111111111111111111";
        var log = new USDtTransferMatcher.TransferLogSnapshot(destination, destination, 1, "0xabc", "3", false, 7);
        Assert.Throws<InvalidOperationException>(() => USDtTransferMatcher.ToTransferMatchSnapshots(
            [log, log with { Value = 2 }], [destination]));
    }

    [Theory]
    [InlineData("ABC-3")]
    [InlineData("ABC-3-LOG-7")]
    public void ReplayPreservesExactStoredIdDespiteCasing(string storedId)
    {
        const string destination = "0x1111111111111111111111111111111111111111";
        var log = new USDtTransferMatcher.TransferLogSnapshot(destination, destination, 1, "0xabc", "3", false, 7);
        var replay = USDtTransferMatcher.ToTransferMatchSnapshots([log], [destination],
            [new USDtTransferMatcher.ExistingTransferSnapshot(storedId, destination, 1)]);
        Assert.Equal(storedId, Assert.Single(replay).TransactionId);
    }

    [Fact]
    public void IdenticalStoredEntriesAreDeduplicated()
    {
        const string destination = "0x1111111111111111111111111111111111111111";
        var log = new USDtTransferMatcher.TransferLogSnapshot(destination, destination, 1, "0xabc", "3", false, 7);
        var stored = new USDtTransferMatcher.ExistingTransferSnapshot("abc-3", destination, 1);
        var replay = USDtTransferMatcher.ToTransferMatchSnapshots([log], [destination], [stored, stored]);
        Assert.Equal(stored.TransactionId, Assert.Single(replay).TransactionId);
    }

    [Theory]
    [InlineData("abc-3", 2)]
    [InlineData("ABC-3", 1)]
    public void ConflictingStoredEntriesFailWithPaymentId(string secondId, int secondAmount)
    {
        const string destination = "0x1111111111111111111111111111111111111111";
        var log = new USDtTransferMatcher.TransferLogSnapshot(destination, destination, 1, "0xabc", "3", false, 7);
        var error = Assert.Throws<InvalidOperationException>(() => USDtTransferMatcher.ToTransferMatchSnapshots([log], [destination],
            [new USDtTransferMatcher.ExistingTransferSnapshot("abc-3", destination, 1),
             new USDtTransferMatcher.ExistingTransferSnapshot(secondId, destination, secondAmount)]));
        Assert.Contains(secondId, error.Message);
    }

    [Fact]
    public void RpcHashCasingDoesNotChangeNewIdsOrDuplicateLogs()
    {
        const string destination = "0x1111111111111111111111111111111111111111";
        var log = new USDtTransferMatcher.TransferLogSnapshot(destination, destination, 1, "0xabc", "3", false, 7);
        var initial = Assert.Single(USDtTransferMatcher.ToTransferMatchSnapshots([log], [destination]));
        var replay = USDtTransferMatcher.ToTransferMatchSnapshots([log, log with { TransactionHash = "0XABC" }], [destination]);
        Assert.Equal(initial, Assert.Single(replay));
    }

    [Theory]
    [InlineData("ABC-3")]
    [InlineData("ABC-3-LOG-7")]
    public void StoredMismatchReportsExactPaymentId(string storedId)
    {
        const string destination = "0x1111111111111111111111111111111111111111";
        var log = new USDtTransferMatcher.TransferLogSnapshot(destination, destination, 1, "0xabc", "3", false, 7);
        var error = Assert.Throws<InvalidOperationException>(() => USDtTransferMatcher.ToTransferMatchSnapshots([log], [destination],
            [new USDtTransferMatcher.ExistingTransferSnapshot(storedId, destination, 2)]));
        Assert.Contains(storedId, error.Message);
    }
}
