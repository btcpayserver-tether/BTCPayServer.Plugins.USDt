using BTCPayServer.Plugins.USDt.Services;
using BTCPayServer.Plugins.USDt.Services.Payments;
using BTCPayServer.Tests;
using BTCPayServer.Client.Models;
using Xunit;
using Xunit.Abstractions;

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
}
