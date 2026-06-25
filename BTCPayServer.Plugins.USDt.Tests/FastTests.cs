using System.Numerics;
using System.Security.Claims;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Services;
using BTCPayServer.Plugins.USDt.Configuration.EVM;
using BTCPayServer.Plugins.USDt.Configuration.Tron;
using BTCPayServer.Plugins.USDt.Services.Payments;
using BTCPayServer.Tests;
using BTCPayServer.Client.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BTCPayServer.Plugins.USDt.Tests;

[Trait("Fast", "Fast")]
public class FastTests : UnitTestBase
{
    public FastTests(ITestOutputHelper helper) : base(helper)
    {
    }

    
    [Fact]
    public void TronConversion()
    {
        Assert.Equal("TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs",TronUSDtAddressHelper.HexToBase58("0x42a1e39aefa49290f2b3f9ed688d7cecf86cd6e0"));
        Assert.Equal("0x42a1e39aefa49290f2b3f9ed688d7cecf86cd6e0",TronUSDtAddressHelper.Base58ToHex("TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs"));
        Assert.True(TronUSDtAddressHelper.IsValid("TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs"));
        Assert.False(TronUSDtAddressHelper.IsValid("TG2XXyExBkPp9nzdajDZsozEu4BkaSJozs"));
        Assert.False(TronUSDtAddressHelper.IsValid("TG3xXyExBkPp9nzdajDZsozEu4BkaSJozs"));
    }

    [Fact]
    public void TronPaymentLinkIncludesAmountByDefault()
    {
        var result = TronUSDtPaymentLinkExtension.BuildPaymentLink(
            "TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs",
            12.34m,
            false);

        Assert.Equal("TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs?amount=12.34", result);
    }

    [Fact]
    public void TronPaymentLinkCanExcludeAmount()
    {
        var result = TronUSDtPaymentLinkExtension.BuildPaymentLink(
            "TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs",
            12.34m,
            true);

        Assert.Equal("TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs", result);
    }

    [Fact]
    public void TronPaymentLinkReturnsNullWithoutDestination()
    {
        Assert.Null(TronUSDtPaymentLinkExtension.BuildPaymentLink(null, 12.34m, false));
        Assert.Null(TronUSDtPaymentLinkExtension.BuildPaymentLink(string.Empty, 12.34m, false));
    }

    [Fact]
    public void EvmPaymentLinkUsesBaseUnitsAndLowercasesAddresses()
    {
        var result = EVMUSDtPaymentLinkExtension.BuildPaymentLink(
            "0x742d35Cc6634C0532925a3b844Bc454e4438f44e",
            "0xdAC17F958D2ee523A2206206994597C13D831ec7",
            1,
            6,
            12.3456789m);

        Assert.Equal(
            "ethereum:0xdac17f958d2ee523a2206206994597c13d831ec7@1/transfer?address=0x742d35cc6634c0532925a3b844bc454e4438f44e&uint256=12345678",
            result);
    }

    [Fact]
    public void EvmPaymentLinkClampsNegativeAmountsToZero()
    {
        var result = EVMUSDtPaymentLinkExtension.BuildPaymentLink(
            "0x742d35Cc6634C0532925a3b844Bc454e4438f44e",
            "0xdAC17F958D2ee523A2206206994597C13D831ec7",
            1,
            6,
            -1m);

        Assert.Equal(
            "ethereum:0xdac17f958d2ee523a2206206994597c13d831ec7@1/transfer?address=0x742d35cc6634c0532925a3b844bc454e4438f44e&uint256=0",
            result);
    }

    [Fact]
    public void EvmPaymentLinkReturnsNullWithoutDestination()
    {
        Assert.Null(EVMUSDtPaymentLinkExtension.BuildPaymentLink(null, "0xdac17f958d2ee523a2206206994597c13d831ec7", 1, 6, 1m));
        Assert.Null(EVMUSDtPaymentLinkExtension.BuildPaymentLink(string.Empty, "0xdac17f958d2ee523a2206206994597c13d831ec7", 1, 6, 1m));
    }

    [Fact]
    public void EvmPaymentLinkReturnsNullWhenSmartContractIsUnset()
    {
        Assert.Null(EVMUSDtPaymentLinkExtension.BuildPaymentLink(
            "0x742d35Cc6634C0532925a3b844Bc454e4438f44e",
            EVMUSDtLikeConfigurationItem.UnconfiguredSmartContractAddress,
            80002,
            6,
            1m));
    }

    [Fact]
    public void EvmPaymentLinkTruncatesFractionalBaseUnits()
    {
        var result = EVMUSDtPaymentLinkExtension.BuildPaymentLink(
            "0x742d35Cc6634C0532925a3b844Bc454e4438f44e",
            "0xdAC17F958D2ee523A2206206994597C13D831ec7",
            1,
            6,
            0.0000009m);

        Assert.Equal(
            "ethereum:0xdac17f958d2ee523a2206206994597c13d831ec7@1/transfer?address=0x742d35cc6634c0532925a3b844bc454e4438f44e&uint256=0",
            result);
    }

    [Fact]
    public void UsdtPaymentConfirmedUsesExpectedThresholds()
    {
        var paymentData = new USDtPaymentData
        {
            TransactionId = "txid",
            BlockHeight = 1,
            To = "to",
            From = "from"
        };

        paymentData.ConfirmationCount = 1;
        Assert.False(paymentData.PaymentConfirmed(SpeedPolicy.HighSpeed));

        paymentData.ConfirmationCount = 2;
        Assert.True(paymentData.PaymentConfirmed(SpeedPolicy.HighSpeed));

        paymentData.ConfirmationCount = 5;
        Assert.False(paymentData.PaymentConfirmed(SpeedPolicy.MediumSpeed));

        paymentData.ConfirmationCount = 6;
        Assert.True(paymentData.PaymentConfirmed(SpeedPolicy.MediumSpeed));

        paymentData.ConfirmationCount = 11;
        Assert.False(paymentData.PaymentConfirmed(SpeedPolicy.LowMediumSpeed));

        paymentData.ConfirmationCount = 12;
        Assert.True(paymentData.PaymentConfirmed(SpeedPolicy.LowMediumSpeed));

        paymentData.ConfirmationCount = 19;
        Assert.False(paymentData.PaymentConfirmed(SpeedPolicy.LowSpeed));

        paymentData.ConfirmationCount = 20;
        Assert.True(paymentData.PaymentConfirmed(SpeedPolicy.LowSpeed));
    }

    [Fact]
    public void EvmConfigurationDetectsUnsetSmartContractAddress()
    {
        var invalidConfig = new EVMUSDtLikeConfigurationItem("Amoy")
        {
            JsonRpcUri = new Uri("https://rpc-amoy.polygon.technology/"),
            SmartContractAddress = EVMUSDtLikeConfigurationItem.UnconfiguredSmartContractAddress,
            Currency = "USDt",
            DisplayName = "USDt on Amoy",
            Divisibility = 6,
            CryptoImagePath = "icon",
            BlockExplorerLink = "https://amoy.polygonscan.com/tx/{0}",
            DefaultRateRules = [],
            CurrencyDisplayName = "USD₮",
            ChainId = 80002
        };

        var validConfig = invalidConfig with { SmartContractAddress = "0x1234567890123456789012345678901234567890" };

        Assert.False(invalidConfig.HasValidSmartContractAddress());
        Assert.True(validConfig.HasValidSmartContractAddress());
    }

    [Fact]
    public void UsdtPaymentMethodConfigDoesNotActivateExcludedEmptyConfigs()
    {
        var config = new USDtPaymentMethodConfig();

        Assert.False(config.ActivatesChain(true));
        Assert.False(USDtChainActivationService.IsActivated(null, false));
    }

    [Fact]
    public void UsdtPaymentMethodConfigActivatesLegacyEnabledConfigs()
    {
        var config = new USDtPaymentMethodConfig();

        Assert.True(config.ActivatesChain(false));
    }

    [Fact]
    public void UsdtPaymentMethodConfigActivatesLegacyConfigsWithAddresses()
    {
        var config = new USDtPaymentMethodConfig
        {
            Addresses = ["TNPeeaaFB7K9cmo4uQpcU32zGK8G1NYqeL"]
        };

        Assert.True(config.ActivatesChain(true));
    }

    [Fact]
    public void UsdtPaymentMethodConfigKeepsChainActivatedAfterAddressesAreRemoved()
    {
        var config = new USDtPaymentMethodConfig
        {
            Addresses = ["TNPeeaaFB7K9cmo4uQpcU32zGK8G1NYqeL"]
        };

        config.MarkActivated();
        config.Addresses = [];

        Assert.True(config.ActivatesChain(true));
    }

    [Fact]
    public void UsdtPaymentMethodConfigPreservesActivationFromPreviousAddresses()
    {
        var previousConfig = new USDtPaymentMethodConfig
        {
            Addresses = ["TNPeeaaFB7K9cmo4uQpcU32zGK8G1NYqeL"]
        };
        var config = new USDtPaymentMethodConfig();

        config.PreserveActivationFrom(previousConfig);

        Assert.True(config.Activated);
        Assert.True(config.ActivatesChain(true));
    }

    [Fact]
    public async Task EvmHandlerPreservesActivationWhenGreenfieldUpdateRemovesLastAddress()
    {
        var handler = new EVMUSDtPaymentMethodHandler(CreateEvmConfiguration(), null!, null!, null!);
        var previousConfig = JObject.FromObject(new EVMUSDtPaymentMethodConfig
        {
            Addresses = ["0x742d35cc6634c0532925a3b844bc454e4438f44e"]
        }, handler.Serializer);
        var incomingConfig = JObject.FromObject(new EVMUSDtPaymentMethodConfig(), handler.Serializer);
        var context = CreateValidationContext(incomingConfig, previousConfig);

        await handler.ValidatePaymentMethodConfig(context);

        var parsedConfig =
            Assert.IsType<EVMUSDtPaymentMethodConfig>(((IPaymentMethodHandler)handler).ParsePaymentMethodConfig(context.Config));
        Assert.True(parsedConfig.Activated);
        Assert.Empty(parsedConfig.Addresses);
    }

    [Fact]
    public async Task TronHandlerMarksIncomingAddressConfigsActivated()
    {
        var handler = new TronUSDtLikePaymentMethodHandler(CreateTronConfiguration(), null!, null!, null!);
        var incomingConfig = JObject.FromObject(new TronUSDtPaymentMethodConfig
        {
            Addresses = ["TNPeeaaFB7K9cmo4uQpcU32zGK8G1NYqeL"]
        }, handler.Serializer);
        var context = CreateValidationContext(incomingConfig);

        await handler.ValidatePaymentMethodConfig(context);

        var parsedConfig =
            Assert.IsType<TronUSDtPaymentMethodConfig>(((IPaymentMethodHandler)handler).ParsePaymentMethodConfig(context.Config));
        Assert.True(parsedConfig.Activated);
        Assert.Equal(["TNPeeaaFB7K9cmo4uQpcU32zGK8G1NYqeL"], parsedConfig.Addresses);
    }

    [Fact]
    public void EvmListenerBatchesDestinationFiltersToReduceRpcFanOut()
    {
        var destinationKeys = Enumerable.Range(0, 45)
            .Select(i => $"0x{i:X40}")
            .ToArray();

        var batches = EVMUSDtListener.BatchDestinationAddresses(destinationKeys, 20);

        Assert.Equal(3, batches.Count);
        Assert.Equal(20, batches[0].Length);
        Assert.Equal(20, batches[1].Length);
        Assert.Equal(5, batches[2].Length);
        Assert.All(batches.SelectMany(batch => batch), address => Assert.Equal(address.ToLowerInvariant(), address));
    }

    [Fact]
    public void EvmListenerTransferPipelineFiltersAndNormalizesTrackedTransfers()
    {
        var trackedAddresses = new[]
        {
            "0x742d35cc6634c0532925a3b844bc454e4438f44e",
            "0x1111111111111111111111111111111111111111"
        };

        var matches = EVMUSDtListener.ToTransferMatchSnapshots(
            [
                new EVMUSDtListener.TransferLogSnapshot(
                    "0x742D35Cc6634C0532925a3b844Bc454e4438f44E",
                    "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    123,
                    "0xabc",
                    "1",
                    false),
                new EVMUSDtListener.TransferLogSnapshot(
                    "0x9999999999999999999999999999999999999999",
                    "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    456,
                    "0xdef",
                    "2",
                    false),
                new EVMUSDtListener.TransferLogSnapshot(
                    "0x1111111111111111111111111111111111111111",
                    "0xcccccccccccccccccccccccccccccccccccccccc",
                    789,
                    "0xghi",
                    "3",
                    true)
            ],
            trackedAddresses);

        var match = Assert.Single(matches);
        Assert.Equal(trackedAddresses[0], match.DestinationKey);
        Assert.Equal("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", match.From);
        Assert.Equal("0x742D35Cc6634C0532925a3b844Bc454e4438f44E", match.To);
        Assert.Equal(new BigInteger(123), match.TotalAmount);
        Assert.Equal("abc-1", match.TransactionId);
    }

    [Fact]
    public void EvmListenerDetectsHeadLagEthGetLogsErrors()
    {
        var exception = new Exception("block range extends beyond current head block: eth_getLogs");

        Assert.True(EVMUSDtListener.IsBlockRangeBeyondCurrentHeadError(exception));
    }

    [Fact]
    public void EvmListenerIgnoresUnrelatedEthErrors()
    {
        var exception = new Exception("execution reverted: eth_call");

        Assert.False(EVMUSDtListener.IsBlockRangeBeyondCurrentHeadError(exception));
    }

    [Theory]
    [InlineData("POLYGON", 2)]
    [InlineData("AMOY", 2)]
    [InlineData("ETHEREUM", 1)]
    public void EvmListenerUsesExpectedHeadLagPerChain(string chain, long expectedLag)
    {
        var configuration = new EVMUSDtLikeConfigurationItem(chain)
        {
            JsonRpcUri = new Uri("https://example.com"),
            SmartContractAddress = "0x1234567890123456789012345678901234567890",
            Currency = "USDt",
            DisplayName = $"USDt on {chain}",
            Divisibility = 6,
            CryptoImagePath = "icon",
            BlockExplorerLink = "https://example.com/tx/{0}",
            DefaultRateRules = [],
            CurrencyDisplayName = "USD₮",
            ChainId = 1
        };

        Assert.Equal(expectedLag, TestableEvmListener.GetHeadLag(configuration));
    }

    private sealed class TestableEvmListener : EVMUSDtListener
    {
        public TestableEvmListener()
            : base(null!, null!, null!, null!, null!, null!, null!, null!, null!)
        {
        }

        public static long GetHeadLag(EVMUSDtLikeConfigurationItem configuration)
        {
            return new TestableEvmListener().GetHeadLagBlocks(configuration);
        }
    }

    private static PaymentMethodConfigValidationContext CreateValidationContext(JToken config, JToken? previousConfig = null)
    {
        return new PaymentMethodConfigValidationContext(
            null!,
            new ModelStateDictionary(),
            config,
            new ClaimsPrincipal(),
            previousConfig);
    }

    private static EVMUSDtLikeConfigurationItem CreateEvmConfiguration()
    {
        return new EVMUSDtLikeConfigurationItem("ETHEREUM")
        {
            JsonRpcUri = new Uri("https://example.com"),
            SmartContractAddress = "0x1234567890123456789012345678901234567890",
            Currency = "USDt",
            DisplayName = "USDt on ETHEREUM",
            Divisibility = 6,
            CryptoImagePath = "icon",
            BlockExplorerLink = "https://example.com/tx/{0}",
            DefaultRateRules = [],
            CurrencyDisplayName = "USD₮",
            ChainId = 1
        };
    }

    private static TronUSDtLikeConfigurationItem CreateTronConfiguration()
    {
        return new TronUSDtLikeConfigurationItem
        {
            JsonRpcUri = new Uri("https://example.com"),
            SmartContractAddress = "TXYZopYRdj2D9XRtbG411XZZ3kM5VkAeBf",
            Currency = "USDt",
            DisplayName = "USDt on TRON",
            Divisibility = 6,
            CryptoImagePath = "icon",
            BlockExplorerLink = "https://example.com/tx/{0}",
            DefaultRateRules = [],
            CurrencyDisplayName = "USD₮"
        };
    }
}
