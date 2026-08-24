using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.EVM;
using BTCPayServer.Plugins.USDt.Configuration.Tron;
using BTCPayServer.Plugins.USDt.Services.Payments;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BTCPayServer.Plugins.USDt.Tests;

[Trait("Fast", "Fast")]
public class PaymentLinkExtensionTests
{
    private const string TronDestination = "TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs";
    private const string EvmDestination = "0x742d35Cc6634C0532925a3b844Bc454e4438f44e";
    private const string EvmDestinationLower = "0x742d35cc6634c0532925a3b844bc454e4438f44e";
    private const string EvmContractLower = "0x1234567890123456789012345678901234567890";

    [Theory]
    [InlineData(USDtPaymentLinkFormat.Standard, null,
        "tron:TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs?amount=12.34")]
    [InlineData(USDtPaymentLinkFormat.StandardWithoutAmount, null,
        "tron:TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs")]
    [InlineData(USDtPaymentLinkFormat.AddressOnly, null,
        TronDestination)]
    [InlineData(USDtPaymentLinkFormat.Custom, "wallet:{to}?amount={amount}&units={amountUnits}",
        "wallet:TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs?amount=12.34&units=12340000")]
    public void TronCheckoutReadsEveryNumericFormatWrittenByHandler(
        USDtPaymentLinkFormat format,
        string? template,
        string expected)
    {
        var (paymentMethodId, configuration, extension) = CreateTronExtension();
        var handler = new TronUSDtLikePaymentMethodHandler(configuration, null!, null!, null!);
        var details = TronUSDtLikePaymentMethodHandler.CreatePaymentPromptDetails(new TronUSDtPaymentMethodConfig
        {
            PaymentLinkFormat = format,
            PaymentLinkTemplate = template
        });
        var serializedDetails = JObject.FromObject(details, handler.Serializer);

        Assert.Equal(JTokenType.Integer, serializedDetails["paymentLinkFormat"]?.Type);

        var prompt = CreatePrompt(paymentMethodId, TronDestination, serializedDetails);
        Assert.Equal(expected, extension.GetPaymentLink(prompt, null));
    }

    [Theory]
    [InlineData(USDtPaymentLinkFormat.Standard, null,
        "ethereum:0x1234567890123456789012345678901234567890@1/transfer?address=0x742d35cc6634c0532925a3b844bc454e4438f44e&uint256=12340000")]
    [InlineData(USDtPaymentLinkFormat.AddressOnly, null,
        EvmDestinationLower)]
    [InlineData(USDtPaymentLinkFormat.Custom, "wallet:{to}?chain={chainId}&units={amountUnits}",
        "wallet:0x742d35cc6634c0532925a3b844bc454e4438f44e?chain=1&units=12340000")]
    public void EvmCheckoutReadsEveryNumericFormatWrittenByHandler(
        USDtPaymentLinkFormat format,
        string? template,
        string expected)
    {
        var (paymentMethodId, configuration, extension) = CreateEvmExtension();
        var handler = new EVMUSDtPaymentMethodHandler(configuration, null!, null!, null!);
        var details = EVMUSDtPaymentMethodHandler.CreatePaymentPromptDetails(new EVMUSDtPaymentMethodConfig
        {
            PaymentLinkFormat = format,
            PaymentLinkTemplate = template
        });
        var serializedDetails = JObject.FromObject(details, handler.Serializer);

        Assert.Equal(JTokenType.Integer, serializedDetails["paymentLinkFormat"]?.Type);

        var prompt = CreatePrompt(paymentMethodId, EvmDestination, serializedDetails);
        Assert.Equal(expected, extension.GetPaymentLink(prompt, null));
    }

    [Theory]
    [InlineData("Standard", "tron:TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs?amount=12.34")]
    [InlineData("StandardWithoutAmount", "tron:TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs")]
    [InlineData("AddressOnly", TronDestination)]
    [InlineData("Custom", "custom:TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs")]
    public void TronCheckoutAcceptsStringEnumRepresentations(string format, string expected)
    {
        var (paymentMethodId, _, extension) = CreateTronExtension();
        var details = JObject.Parse($$"""
                                    {
                                      "paymentLinkFormat": "{{format}}",
                                      "paymentLinkTemplate": "custom:{to}",
                                      "excludeAmountFromPaymentLink": false
                                    }
                                    """);

        var prompt = CreatePrompt(paymentMethodId, TronDestination, details);
        Assert.Equal(expected, extension.GetPaymentLink(prompt, null));
    }

    [Theory]
    [InlineData("Standard",
        "ethereum:0x1234567890123456789012345678901234567890@1/transfer?address=0x742d35cc6634c0532925a3b844bc454e4438f44e&uint256=12340000")]
    [InlineData("AddressOnly", EvmDestinationLower)]
    [InlineData("Custom", "custom:0x742d35cc6634c0532925a3b844bc454e4438f44e")]
    public void EvmCheckoutAcceptsStringEnumRepresentations(string format, string expected)
    {
        var (paymentMethodId, _, extension) = CreateEvmExtension();
        var details = JObject.Parse($$"""
                                    {
                                      "paymentLinkFormat": "{{format}}",
                                      "paymentLinkTemplate": "custom:{to}"
                                    }
                                    """);

        var prompt = CreatePrompt(paymentMethodId, EvmDestination, details);
        Assert.Equal(expected, extension.GetPaymentLink(prompt, null));
    }

    [Theory]
    [InlineData(false, "tron:TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs?amount=12.34")]
    [InlineData(true, "tron:TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs")]
    public void TronCheckoutPreservesLegacyInvoicesWithoutFormat(bool excludeAmount, string expected)
    {
        var (paymentMethodId, _, extension) = CreateTronExtension();
        var details = JObject.FromObject(new { excludeAmountFromPaymentLink = excludeAmount });

        var prompt = CreatePrompt(paymentMethodId, TronDestination, details);
        Assert.Equal(expected, extension.GetPaymentLink(prompt, null));
    }

    [Fact]
    public void EvmCheckoutPreservesLegacyInvoicesWithoutFormat()
    {
        var (paymentMethodId, _, extension) = CreateEvmExtension();
        var prompt = CreatePrompt(paymentMethodId, EvmDestination, new JObject());

        Assert.Equal(
            "ethereum:0x1234567890123456789012345678901234567890@1/transfer?address=0x742d35cc6634c0532925a3b844bc454e4438f44e&uint256=12340000",
            extension.GetPaymentLink(prompt, null));
    }

    [Fact]
    public void TronCheckoutPreservesTemplateOnlyLegacyInvoices()
    {
        var (paymentMethodId, _, extension) = CreateTronExtension();
        var details = JObject.Parse("""{"paymentLinkTemplate":"legacy:{to}"}""");

        var prompt = CreatePrompt(paymentMethodId, TronDestination, details);
        Assert.Equal("legacy:TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs", extension.GetPaymentLink(prompt, null));
    }

    [Fact]
    public void EvmCheckoutPreservesTemplateOnlyLegacyInvoices()
    {
        var (paymentMethodId, _, extension) = CreateEvmExtension();
        var details = JObject.Parse("""{"paymentLinkTemplate":"legacy:{to}"}""");

        var prompt = CreatePrompt(paymentMethodId, EvmDestination, details);
        Assert.Equal("legacy:0x742d35cc6634c0532925a3b844bc454e4438f44e", extension.GetPaymentLink(prompt, null));
    }

    [Theory]
    [InlineData("999")]
    [InlineData("\"Unknown\"")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("true")]
    public void InvalidTronFormatsFallBackToStandardWithoutThrowing(string formatJson)
    {
        var (paymentMethodId, _, extension) = CreateTronExtension();
        var details = JObject.Parse($$"""
                                    {
                                      "paymentLinkFormat": {{formatJson}},
                                      "paymentLinkTemplate": "custom:{to}",
                                      "excludeAmountFromPaymentLink": true
                                    }
                                    """);

        var prompt = CreatePrompt(paymentMethodId, TronDestination, details);
        Assert.Equal(
            "tron:TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs?amount=12.34",
            extension.GetPaymentLink(prompt, null));
    }

    [Theory]
    [InlineData("999")]
    [InlineData("\"Unknown\"")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("true")]
    [InlineData("1")]
    public void InvalidOrUnsupportedEvmFormatsFallBackToStandardWithoutThrowing(string formatJson)
    {
        var (paymentMethodId, _, extension) = CreateEvmExtension();
        var details = JObject.Parse($$"""
                                    {
                                      "paymentLinkFormat": {{formatJson}},
                                      "paymentLinkTemplate": "custom:{to}"
                                    }
                                    """);

        var prompt = CreatePrompt(paymentMethodId, EvmDestination, details);
        Assert.Equal(
            "ethereum:0x1234567890123456789012345678901234567890@1/transfer?address=0x742d35cc6634c0532925a3b844bc454e4438f44e&uint256=12340000",
            extension.GetPaymentLink(prompt, null));
    }

    [Fact]
    public void NullFormatRetainsTemplateOnlyCompatibilityOnBothFamilies()
    {
        var details = JObject.Parse("""
                                    {
                                      "paymentLinkFormat": null,
                                      "paymentLinkTemplate": "legacy:{to}"
                                    }
                                    """);

        var (tronPaymentMethodId, _, tronExtension) = CreateTronExtension();
        var tronPrompt = CreatePrompt(tronPaymentMethodId, TronDestination, details);
        Assert.Equal("legacy:TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs", tronExtension.GetPaymentLink(tronPrompt, null));

        var (evmPaymentMethodId, _, evmExtension) = CreateEvmExtension();
        var evmPrompt = CreatePrompt(evmPaymentMethodId, EvmDestination, details);
        Assert.Equal("legacy:0x742d35cc6634c0532925a3b844bc454e4438f44e", evmExtension.GetPaymentLink(evmPrompt, null));
    }

    private static (PaymentMethodId PaymentMethodId, TronUSDtLikeConfigurationItem Configuration,
        TronUSDtPaymentLinkExtension Extension) CreateTronExtension()
    {
        var configuration = new TronUSDtLikeConfigurationItem
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
        var paymentMethodId = configuration.GetPaymentMethodId();
        var pluginConfiguration = new USDtPluginConfiguration();
        pluginConfiguration.TronUSDtLikeConfigurationItems.Add(paymentMethodId, configuration);
        return (paymentMethodId, configuration,
            new TronUSDtPaymentLinkExtension(paymentMethodId, pluginConfiguration));
    }

    private static (PaymentMethodId PaymentMethodId, EVMUSDtLikeConfigurationItem Configuration,
        EVMUSDtPaymentLinkExtension Extension) CreateEvmExtension()
    {
        var configuration = new EVMUSDtLikeConfigurationItem("ETHEREUM")
        {
            JsonRpcUri = new Uri("https://example.com"),
            SmartContractAddress = EvmContractLower,
            Currency = "USDt",
            DisplayName = "USDt on ETHEREUM",
            Divisibility = 6,
            CryptoImagePath = "icon",
            BlockExplorerLink = "https://example.com/tx/{0}",
            DefaultRateRules = [],
            CurrencyDisplayName = "USD₮",
            ChainId = 1
        };
        var paymentMethodId = configuration.GetPaymentMethodId();
        var pluginConfiguration = new USDtPluginConfiguration();
        pluginConfiguration.EVMUSDtLikeConfigurationItems.Add(paymentMethodId, configuration);
        return (paymentMethodId, configuration,
            new EVMUSDtPaymentLinkExtension(paymentMethodId, pluginConfiguration));
    }

    private static PaymentPrompt CreatePrompt(PaymentMethodId paymentMethodId, string destination, JToken details)
    {
        var invoice = new InvoiceEntity
        {
            Currency = "USD",
            Price = 12.34m
        };
#pragma warning disable CS0618
        invoice.Payments = [];
        invoice.Rates["USDt"] = 1m;
#pragma warning restore CS0618
        var prompt = new PaymentPrompt
        {
            Currency = "USDt",
            Destination = destination,
            Divisibility = 6,
            Details = details
        };
        invoice.SetPaymentPrompt(paymentMethodId, prompt);
        invoice.UpdateTotals();
        return prompt;
    }
}
