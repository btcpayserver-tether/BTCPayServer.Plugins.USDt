using System.Globalization;
using System.Numerics;
using Nethereum.JsonRpc.Client;
using System.Security.Claims;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Controllers;
using BTCPayServer.Plugins.USDt.Services;
using BTCPayServer.Plugins.USDt.Configuration.EVM;
using BTCPayServer.Plugins.USDt.Configuration.Tron;
using BTCPayServer.Plugins.USDt.Services.Payments;
using BTCPayServer.Tests;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Configuration;
using Nethereum.Hex.HexTypes;
using NBitcoin;
using NBXplorer;
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
    public void TronPollingUsesConfiguredBlockTime()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), USDtListenerShared.GetBlockPollingDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(1), USDtListenerShared.GetBlockPollingDelay(0.25));
    }

    [Fact]
    public void RateLimitBackoffIsJitteredAndCapped()
    {
        Assert.Equal(USDtListenerShared.InitialRateLimitBackoffMs, 5_000);
        Assert.Equal(5_000, USDtListenerShared.CalculateRateLimitDelayMs(5_000, 0));
        Assert.Equal(5_500, USDtListenerShared.CalculateRateLimitDelayMs(5_000, 0.5));
        Assert.Equal(6_000, USDtListenerShared.CalculateRateLimitDelayMs(5_000, 1));
        Assert.Equal(60_000, USDtListenerShared.CalculateRateLimitDelayMs(60_000, 1));

        Assert.Equal(10_000, USDtListenerShared.GetNextRateLimitBackoffMs(5_000));
        Assert.Equal(60_000, USDtListenerShared.GetNextRateLimitBackoffMs(40_000));
        Assert.Equal(60_000, USDtListenerShared.GetNextRateLimitBackoffMs(60_000));
    }

    [Theory]
    [InlineData("Response status code does not indicate success: 429 (Too Many Requests).", true)]
    [InlineData("403 Forbidden: The key exceeds the frequency limit", true)]
    [InlineData("403 Forbidden: invalid API key", false)]
    [InlineData("500 Internal Server Error", false)]
    public void RateLimitDetectionIsSelective(string message, bool expected)
    {
        var exception = new RpcClientUnknownException("RPC failure", new HttpRequestException(message));

        Assert.Equal(expected, USDtListenerShared.IsRateLimitException(exception));
    }

    [Fact]
    public async Task TrackedInvoicesMergePendingAndExpiredWithinGraceAfterRestart()
    {
        var now = DateTimeOffset.Parse("2026-08-21T12:00:00Z");
        var paymentMethodId = new PaymentMethodId("USDT-TRON");
        var source = new TestInvoiceSource
        {
            MonitoredInvoices =
            [
                CreateInvoice("new", InvoiceStatus.New, now.AddHours(1), paymentMethodId, "TNew"),
                CreateInvoice("processing", InvoiceStatus.Processing, now.AddHours(1), paymentMethodId, "TProcessing")
            ],
            ExpiredInvoices =
            [
                CreateInvoice("grace", InvoiceStatus.Expired, now.AddMinutes(10), paymentMethodId, "TGrace"),
                CreateInvoice("past-grace", InvoiceStatus.Expired, now.AddSeconds(-1), paymentMethodId, "TPast"),
                CreateInvoice("settled", InvoiceStatus.Settled, now.AddMinutes(10), paymentMethodId, "TSettled"),
                CreateInvoice("invalid", InvoiceStatus.Invalid, now.AddMinutes(10), paymentMethodId, "TInvalid")
            ]
        };
        var provider = new USDtTrackedInvoiceProvider(source, new TestTimeProvider(now));

        var tracked = await provider.GetTrackedInvoices(paymentMethodId, TestContext.Current.CancellationToken);

        Assert.Equal(["grace", "new", "processing"], tracked.Select(invoice => invoice.Id).Order().ToArray());
        Assert.Equal(now - USDtTrackedInvoiceProvider.BootstrapLookback, source.ExpiredStartDate);
        Assert.Equal(1, source.ExpiredQueryCount);
    }

    [Fact]
    public async Task TrackedInvoiceRefreshReleasesSettledInvoices()
    {
        var now = DateTimeOffset.Parse("2026-08-21T12:00:00Z");
        var paymentMethodId = new PaymentMethodId("USDT-TRON");
        var timeProvider = new TestTimeProvider(now);
        var source = new TestInvoiceSource
        {
            MonitoredInvoices =
            [
                CreateInvoice("invoice", InvoiceStatus.New, now.AddHours(1), paymentMethodId, "TAddress")
            ]
        };
        var provider = new USDtTrackedInvoiceProvider(source, timeProvider);
        Assert.Single(await provider.GetTrackedInvoices(paymentMethodId, TestContext.Current.CancellationToken));

        source.MonitoredInvoices = [];
        source.RefreshedInvoices =
        [
            CreateInvoice("invoice", InvoiceStatus.Settled, now.AddHours(1), paymentMethodId, "TAddress")
        ];
        timeProvider.UtcNow += USDtTrackedInvoiceProvider.RefreshInterval;

        Assert.Empty(await provider.GetTrackedInvoices(paymentMethodId, TestContext.Current.CancellationToken));
        Assert.Equal(1, source.RefreshQueryCount);
    }

    [Fact]
    public async Task AddressReservationEndsAtMonitoringExpiration()
    {
        var now = DateTimeOffset.Parse("2026-08-21T12:00:00Z");
        var paymentMethodId = new PaymentMethodId("USDT-TRON");
        var timeProvider = new TestTimeProvider(now);
        var source = new TestInvoiceSource
        {
            ExpiredInvoices =
            [
                CreateInvoice("late", InvoiceStatus.Expired, now.AddMinutes(5), paymentMethodId, "TReserved")
            ]
        };
        var provider = new USDtTrackedInvoiceProvider(source, timeProvider);

        Assert.Equal(
            ["TReserved"],
            await USDtPaymentMethodConfig.GetReservedAddresses(paymentMethodId, provider));

        timeProvider.UtcNow = now.AddMinutes(5);

        Assert.Empty(await USDtPaymentMethodConfig.GetReservedAddresses(paymentMethodId, provider));
    }

    [Fact]
    public async Task ExpiredInvoiceRetainsPaidLateStatusWhileTracked()
    {
        var now = DateTimeOffset.Parse("2026-08-21T12:00:00Z");
        var paymentMethodId = new PaymentMethodId("USDT-TRON");
        var invoice = CreateInvoice(
            "paid-late",
            InvoiceStatus.Expired,
            now.AddMinutes(5),
            paymentMethodId,
            "TLate");
        invoice.ExceptionStatus = InvoiceExceptionStatus.PaidLate;
        var source = new TestInvoiceSource { ExpiredInvoices = [invoice] };
        var provider = new USDtTrackedInvoiceProvider(source, new TestTimeProvider(now));

        var tracked = Assert.Single(
            await provider.GetTrackedInvoices(paymentMethodId, TestContext.Current.CancellationToken));

        Assert.Equal(InvoiceStatus.Expired, tracked.Status);
        Assert.Equal(InvoiceExceptionStatus.PaidLate, tracked.ExceptionStatus);
    }

    [Fact]
    public void BlockTimestampUsesUnixTimeAndRejectsUnavailableValues()
    {
        var expected = DateTimeOffset.Parse("2023-11-14T22:13:20Z");

        Assert.True(USDtListenerShared.TryGetBlockTimestamp(new HexBigInteger(1_700_000_000), out var timestamp));
        Assert.Equal(expected, timestamp);
        Assert.False(USDtListenerShared.TryGetBlockTimestamp(null, out _));
        Assert.False(USDtListenerShared.TryGetBlockTimestamp(new HexBigInteger(0), out _));
    }

    [Fact]
    public void TronStoreSettingsDetectDuplicateSubmittedAddresses()
    {
        var duplicate = UITronUSDtLikeStoreController.FindDuplicateAddress(
        [
            "TQQvC5DuajPSPnDN9UA535Ts4tC1uCJUvJ",
            "TMsbHFUiGrAw13HTqkPekgrseXogWioQ3d",
            "TMsbHFUiGrAw13HTqkPekgrseXogWioQ3d"
        ]);

        Assert.Equal("TMsbHFUiGrAw13HTqkPekgrseXogWioQ3d", duplicate);
    }

    [Fact]
    public void TronStoreSettingsUsesFirstBalanceForLegacyDuplicates()
    {
        const string address = "TMsbHFUiGrAw13HTqkPekgrseXogWioQ3d";

        var balance = UITronUSDtLikeStoreController.FindBalance(
        [
            (address, 10m),
            (address, 20m)
        ], address);

        Assert.Equal(10m, balance);
    }

    [Fact]
    public void TronPaymentLinkIncludesAmountByDefault()
    {
        var result = TronUSDtPaymentLinkExtension.BuildPaymentLink(
            "TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs",
            12.34m,
            false);

        Assert.Equal("tron:TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs?amount=12.34", result);
    }

    [Fact]
    public void TronPaymentLinkCanExcludeAmount()
    {
        var result = TronUSDtPaymentLinkExtension.BuildPaymentLink(
            "TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs",
            12.34m,
            true);

        Assert.Equal("tron:TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs", result);
    }

    [Fact]
    public void TronPaymentLinkCanUseAddressOnlyFormat()
    {
        var result = TronUSDtPaymentLinkExtension.BuildPaymentLink(
            "TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs",
            12.34m,
            USDtPaymentLinkFormat.AddressOnly,
            null,
            "TXLAQ63Xg1NAzckPwKHvzw7CSEmLMEqcdj",
            6);

        Assert.Equal("TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs", result);
    }

    [Fact]
    public void TronCustomPaymentLinkSupportsAllTronPlaceholdersAndPreservesBase58Case()
    {
        var result = TronUSDtPaymentLinkExtension.BuildPaymentLink(
            "TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs",
            12.34m,
            USDtPaymentLinkFormat.Custom,
            "wallet:{to}?amount={amount}&units={amountUnits}&contract={smartContractAddress}",
            "TXLAQ63Xg1NAzckPwKHvzw7CSEmLMEqcdj",
            6);

        Assert.Equal(
            "wallet:TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs?amount=12.34&units=12340000&contract=TXLAQ63Xg1NAzckPwKHvzw7CSEmLMEqcdj",
            result);
    }

    [Fact]
    public void InvalidTronCustomTemplateFallsBackToStandard()
    {
        var result = TronUSDtPaymentLinkExtension.BuildPaymentLink(
            "TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs",
            12.34m,
            USDtPaymentLinkFormat.Custom,
            "wallet:{unknown}",
            "TXLAQ63Xg1NAzckPwKHvzw7CSEmLMEqcdj",
            6);

        Assert.Equal("tron:TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs?amount=12.34", result);
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
    public void EvmPaymentLinkCanUseAddressOnlyFormat()
    {
        var result = EVMUSDtPaymentLinkExtension.BuildPaymentLink(
            "0x742d35Cc6634C0532925a3b844Bc454e4438f44e",
            "0xdAC17F958D2ee523A2206206994597C13D831ec7",
            1,
            6,
            12.34m,
            USDtPaymentLinkFormat.AddressOnly,
            null);

        Assert.Equal("0x742d35cc6634c0532925a3b844bc454e4438f44e", result);
    }

    [Fact]
    public void EvmCustomPaymentLinkSupportsAllEvmPlaceholders()
    {
        var result = EVMUSDtPaymentLinkExtension.BuildPaymentLink(
            "0x742d35Cc6634C0532925a3b844Bc454e4438f44e",
            "0xdAC17F958D2ee523A2206206994597C13D831ec7",
            137,
            6,
            12.34m,
            USDtPaymentLinkFormat.Custom,
            "wallet:{to}?amount={amount}&units={amountUnits}&contract={smartContractAddress}&chain={chainId}");

        Assert.Equal(
            "wallet:0x742d35cc6634c0532925a3b844bc454e4438f44e?amount=12.34&units=12340000&contract=0xdac17f958d2ee523a2206206994597c13d831ec7&chain=137",
            result);
    }

    [Fact]
    public void CustomPaymentLinkRenderingIsCultureInvariant()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var result = EVMUSDtPaymentLinkExtension.BuildPaymentLink(
                "0x742d35Cc6634C0532925a3b844Bc454e4438f44e",
                "0xdAC17F958D2ee523A2206206994597C13D831ec7",
                137,
                6,
                12.34m,
                USDtPaymentLinkFormat.Custom,
                "wallet:{to}?amount={amount}&units={amountUnits}&chain={chainId}");

            Assert.Contains("amount=12.34&units=12340000&chain=137", result);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void CustomTemplateSubstitutionIsNotRecursive()
    {
        var values = new Dictionary<string, string>
        {
            ["{to}"] = "{amount}",
            ["{amount}"] = "12.34",
            ["{amountUnits}"] = "12340000",
            ["{smartContractAddress}"] = "contract"
        };

        var success = USDtPaymentLinkFormats.TryRenderTemplate(
            "wallet:{to}?amount={amount}",
            false,
            values,
            out var rendered,
            out var error);

        Assert.True(success, error);
        Assert.Equal("wallet:{amount}?amount=12.34", rendered);
    }

    [Theory]
    [InlineData(null, "A payment link template is required")]
    [InlineData("wallet:{amount}", "must contain the {to} placeholder")]
    [InlineData("wallet:{to}?value={unknown}", "Unknown or unsupported")]
    [InlineData("wallet:{to", "invalid placeholder expression")]
    [InlineData("wallet:{to}\n", "control characters")]
    public void CustomTemplateValidationRejectsUnsafeTemplates(string? template, string expectedError)
    {
        var error = USDtPaymentLinkFormats.ValidateTemplate(template, false);

        Assert.Contains(expectedError, error);
    }

    [Fact]
    public void TronTemplatesRejectEvmOnlyChainIdPlaceholder()
    {
        var error = USDtPaymentLinkFormats.ValidateTemplate("wallet:{to}?chain={chainId}", false);

        Assert.Contains("Unknown or unsupported", error);
    }

    [Fact]
    public void TemplateValidationEnforcesInputAndRenderedLengthLimits()
    {
        var oversizedTemplate = "{to}" + new string('x', USDtPaymentLinkFormats.MaxTemplateLength);
        Assert.Contains("cannot exceed", USDtPaymentLinkFormats.ValidateTemplate(oversizedTemplate, false));

        var values = new Dictionary<string, string>
        {
            ["{to}"] = new string('x', USDtPaymentLinkFormats.MaxRenderedLength + 1),
            ["{amount}"] = "12.34",
            ["{amountUnits}"] = "12340000",
            ["{smartContractAddress}"] = "contract"
        };
        Assert.False(USDtPaymentLinkFormats.TryRenderTemplate(
            "{to}",
            false,
            values,
            out _,
            out var renderError));
        Assert.Contains("cannot exceed", renderError);
        Assert.Contains(
            "cannot exceed",
            USDtPaymentLinkFormats.ValidateSelection(
                USDtPaymentLinkFormat.Custom,
                "{to}",
                false,
                values));
    }

    [Fact]
    public void PaymentLinkFormatResolutionPreservesLegacyConfigurations()
    {
        Assert.Equal(
            USDtPaymentLinkFormat.Standard,
            USDtPaymentLinkFormats.ResolveTron(null, null, false));
        Assert.Equal(
            USDtPaymentLinkFormat.StandardWithoutAmount,
            USDtPaymentLinkFormats.ResolveTron(null, null, true));
        Assert.Equal(
            USDtPaymentLinkFormat.Custom,
            USDtPaymentLinkFormats.ResolveEvm(null, "wallet:{to}"));
        Assert.Equal(
            USDtPaymentLinkFormat.AddressOnly,
            USDtPaymentLinkFormats.ResolveTron(USDtPaymentLinkFormat.AddressOnly, "wallet:{to}", false));
    }

    [Fact]
    public void EvmRejectsStandardWithoutAmountFormat()
    {
        Assert.Contains(
            "not supported",
            USDtPaymentLinkFormats.ValidateSelection(USDtPaymentLinkFormat.StandardWithoutAmount, null, true));
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
    public void RegtestUsesExternalChainTestnetDefaults()
    {
        var networkProvider = new NBXplorerNetworkProvider(ChainName.Regtest);
        var configuration = new ConfigurationBuilder().Build();

        var tronConfiguration =
            USDtConfigurationProvider.GetTronUSDtLikeDefaultConfigurationItem(networkProvider, configuration);
        Assert.Equal(new Uri("https://nile.trongrid.io/jsonrpc"), tronConfiguration.JsonRpcUri);
        Assert.Equal("TXLAQ63Xg1NAzckPwKHvzw7CSEmLMEqcdj", tronConfiguration.SmartContractAddress);

        var evmConfigurations =
            USDtConfigurationProvider.GetEVMUSDtLikeDefaultConfigurationItems(networkProvider, configuration)
                .Values
                .ToDictionary(config => config.Chain);

        Assert.Equal(11155111, evmConfigurations[Constants.SepoliaChainName].ChainId);
        Assert.Equal(80002, evmConfigurations[Constants.AmoyChainName].ChainId);
        Assert.Equal(97, evmConfigurations[Constants.BscTestnetChainName].ChainId);
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
    public void PaymentPromptDetailsSnapshotFormatAndTemplate()
    {
        var tronConfig = new TronUSDtPaymentMethodConfig
        {
            PaymentLinkFormat = USDtPaymentLinkFormat.Custom,
            PaymentLinkTemplate = "wallet:{to}?amount={amount}"
        };
        var tronDetails = TronUSDtLikePaymentMethodHandler.CreatePaymentPromptDetails(tronConfig);
        tronConfig.PaymentLinkFormat = USDtPaymentLinkFormat.AddressOnly;
        tronConfig.PaymentLinkTemplate = "changed:{to}";

        Assert.Equal(USDtPaymentLinkFormat.Custom, tronDetails.PaymentLinkFormat);
        Assert.Equal("wallet:{to}?amount={amount}", tronDetails.PaymentLinkTemplate);
        Assert.False(tronDetails.ExcludeAmountFromPaymentLink);

        var evmConfig = new EVMUSDtPaymentMethodConfig
        {
            PaymentLinkFormat = USDtPaymentLinkFormat.Custom,
            PaymentLinkTemplate = "wallet:{to}?chain={chainId}"
        };
        var evmDetails = EVMUSDtPaymentMethodHandler.CreatePaymentPromptDetails(evmConfig);
        evmConfig.PaymentLinkFormat = USDtPaymentLinkFormat.Standard;
        evmConfig.PaymentLinkTemplate = null;

        Assert.Equal(USDtPaymentLinkFormat.Custom, evmDetails.PaymentLinkFormat);
        Assert.Equal("wallet:{to}?chain={chainId}", evmDetails.PaymentLinkTemplate);
    }

    [Fact]
    public void PaymentLinkConfigurationSerializationPreservesLegacyAndNewProperties()
    {
        var tronHandler = new TronUSDtLikePaymentMethodHandler(CreateTronConfiguration(), null!, null!, null!);
        var oldConfigToken = JObject.Parse("""{"excludeAmountFromPaymentLink":true}""");
        var oldConfig = Assert.IsType<TronUSDtPaymentMethodConfig>(
            ((IPaymentMethodHandler)tronHandler).ParsePaymentMethodConfig(oldConfigToken));
        Assert.Null(oldConfig.PaymentLinkFormat);
        Assert.Equal(
            USDtPaymentLinkFormat.StandardWithoutAmount,
            USDtPaymentLinkFormats.ResolveTron(
                oldConfig.PaymentLinkFormat,
                oldConfig.PaymentLinkTemplate,
                oldConfig.ExcludeAmountFromPaymentLink));

        var templateOnlyConfig = Assert.IsType<TronUSDtPaymentMethodConfig>(
            ((IPaymentMethodHandler)tronHandler).ParsePaymentMethodConfig(
                JObject.Parse("{\"paymentLinkTemplate\":\"wallet:{to}\"}")));
        Assert.Equal(
            USDtPaymentLinkFormat.Custom,
            USDtPaymentLinkFormats.ResolveTron(
                templateOnlyConfig.PaymentLinkFormat,
                templateOnlyConfig.PaymentLinkTemplate,
                templateOnlyConfig.ExcludeAmountFromPaymentLink));

        var newConfig = new TronUSDtPaymentMethodConfig
        {
            ExcludeAmountFromPaymentLink = true,
            PaymentLinkFormat = USDtPaymentLinkFormat.Custom,
            PaymentLinkTemplate = "wallet:{to}"
        };
        var roundTrip = JObject.FromObject(newConfig, tronHandler.Serializer)
            .ToObject<TronUSDtPaymentMethodConfig>(tronHandler.Serializer);

        Assert.NotNull(roundTrip);
        Assert.True(roundTrip.ExcludeAmountFromPaymentLink);
        Assert.Equal(USDtPaymentLinkFormat.Custom, roundTrip.PaymentLinkFormat);
        Assert.Equal("wallet:{to}", roundTrip.PaymentLinkTemplate);

        var evmHandler = new EVMUSDtPaymentMethodHandler(CreateEvmConfiguration(), null!, null!, null!);
        var evmRoundTrip = JObject.FromObject(new EVMUSDtPaymentMethodConfig
            {
                PaymentLinkFormat = USDtPaymentLinkFormat.AddressOnly,
                PaymentLinkTemplate = "saved:{to}"
            }, evmHandler.Serializer)
            .ToObject<EVMUSDtPaymentMethodConfig>(evmHandler.Serializer);

        Assert.NotNull(evmRoundTrip);
        Assert.Equal(USDtPaymentLinkFormat.AddressOnly, evmRoundTrip.PaymentLinkFormat);
        Assert.Equal("saved:{to}", evmRoundTrip.PaymentLinkTemplate);
    }

    [Fact]
    public void PaymentPromptDetailsSerializationSupportsOldAndNewInvoices()
    {
        var tronHandler = new TronUSDtLikePaymentMethodHandler(CreateTronConfiguration(), null!, null!, null!);
        var oldTronDetails = JObject.Parse("""{"excludeAmountFromPaymentLink":true}""")
            .ToObject<TronUSDtLikeOnChainPaymentMethodDetails>(tronHandler.Serializer);

        Assert.NotNull(oldTronDetails);
        Assert.Null(oldTronDetails.PaymentLinkFormat);
        Assert.Equal(
            USDtPaymentLinkFormat.StandardWithoutAmount,
            USDtPaymentLinkFormats.ResolveTron(
                oldTronDetails.PaymentLinkFormat,
                oldTronDetails.PaymentLinkTemplate,
                oldTronDetails.ExcludeAmountFromPaymentLink));

        var newTronDetails = new TronUSDtLikeOnChainPaymentMethodDetails
        {
            ExcludeAmountFromPaymentLink = true,
            PaymentLinkFormat = USDtPaymentLinkFormat.Custom,
            PaymentLinkTemplate = "wallet:{to}"
        };
        var tronRoundTrip = JObject.FromObject(newTronDetails, tronHandler.Serializer)
            .ToObject<TronUSDtLikeOnChainPaymentMethodDetails>(tronHandler.Serializer);
        Assert.NotNull(tronRoundTrip);
        Assert.Equal(USDtPaymentLinkFormat.Custom, tronRoundTrip.PaymentLinkFormat);
        Assert.Equal("wallet:{to}", tronRoundTrip.PaymentLinkTemplate);

        var evmHandler = new EVMUSDtPaymentMethodHandler(CreateEvmConfiguration(), null!, null!, null!);
        var oldEvmDetails = JObject.Parse("{}")
            .ToObject<EVMUSDtLikeOnChainPaymentMethodDetails>(evmHandler.Serializer);
        Assert.NotNull(oldEvmDetails);
        Assert.Equal(
            USDtPaymentLinkFormat.Standard,
            USDtPaymentLinkFormats.ResolveEvm(
                oldEvmDetails.PaymentLinkFormat,
                oldEvmDetails.PaymentLinkTemplate));

        var newEvmDetails = new EVMUSDtLikeOnChainPaymentMethodDetails
        {
            PaymentLinkFormat = USDtPaymentLinkFormat.Custom,
            PaymentLinkTemplate = "wallet:{to}?chain={chainId}"
        };
        var evmRoundTrip = JObject.FromObject(newEvmDetails, evmHandler.Serializer)
            .ToObject<EVMUSDtLikeOnChainPaymentMethodDetails>(evmHandler.Serializer);
        Assert.NotNull(evmRoundTrip);
        Assert.Equal(USDtPaymentLinkFormat.Custom, evmRoundTrip.PaymentLinkFormat);
        Assert.Equal("wallet:{to}?chain={chainId}", evmRoundTrip.PaymentLinkTemplate);
    }

    [Fact]
    public async Task PaymentMethodHandlersRejectInvalidFormatConfigurations()
    {
        var evmHandler = new EVMUSDtPaymentMethodHandler(CreateEvmConfiguration(), null!, null!, null!);
        var unsupportedEvmConfig = JObject.FromObject(new EVMUSDtPaymentMethodConfig
        {
            PaymentLinkFormat = USDtPaymentLinkFormat.StandardWithoutAmount
        }, evmHandler.Serializer);
        var evmContext = CreateValidationContext(unsupportedEvmConfig);

        await evmHandler.ValidatePaymentMethodConfig(evmContext);

        Assert.False(evmContext.ModelState.IsValid);

        var tronHandler = new TronUSDtLikePaymentMethodHandler(CreateTronConfiguration(), null!, null!, null!);
        var invalidTronConfig = JObject.FromObject(new TronUSDtPaymentMethodConfig
        {
            PaymentLinkFormat = USDtPaymentLinkFormat.Custom,
            PaymentLinkTemplate = "wallet:{chainId}"
        }, tronHandler.Serializer);
        var tronContext = CreateValidationContext(invalidTronConfig);

        await tronHandler.ValidatePaymentMethodConfig(tronContext);

        Assert.False(tronContext.ModelState.IsValid);
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

    private static InvoiceEntity CreateInvoice(
        string id,
        InvoiceStatus status,
        DateTimeOffset monitoringExpiration,
        PaymentMethodId paymentMethodId,
        string destination)
    {
        var invoice = new InvoiceEntity
        {
            Id = id,
            Status = status,
            MonitoringExpiration = monitoringExpiration,
            Currency = "USD",
            Price = 1m
        };
        invoice.SetPaymentPrompt(paymentMethodId, new PaymentPrompt
        {
            Currency = "USDt",
            Destination = destination,
            Divisibility = 6
        });
        return invoice;
    }

    private sealed class TestInvoiceSource : IUSDtInvoiceSource
    {
        public InvoiceEntity[] MonitoredInvoices { get; set; } = [];
        public InvoiceEntity[] ExpiredInvoices { get; set; } = [];
        public InvoiceEntity[] RefreshedInvoices { get; set; } = [];
        public DateTimeOffset? ExpiredStartDate { get; private set; }
        public int ExpiredQueryCount { get; private set; }
        public int RefreshQueryCount { get; private set; }

        public Task<InvoiceEntity[]> GetMonitoredInvoices(
            PaymentMethodId paymentMethodId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(MonitoredInvoices);
        }

        public Task<InvoiceEntity[]> GetExpiredInvoicesSince(
            DateTimeOffset startDate,
            CancellationToken cancellationToken)
        {
            ExpiredStartDate = startDate;
            ExpiredQueryCount++;
            return Task.FromResult(ExpiredInvoices);
        }

        public Task<InvoiceEntity[]> GetInvoices(
            string[] invoiceIds,
            CancellationToken cancellationToken)
        {
            RefreshQueryCount++;
            return Task.FromResult(
                RefreshedInvoices.Where(invoice => invoiceIds.Contains(invoice.Id)).ToArray());
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return UtcNow;
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
