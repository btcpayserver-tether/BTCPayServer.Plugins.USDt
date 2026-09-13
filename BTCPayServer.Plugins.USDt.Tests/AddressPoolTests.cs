using System.Security.Claims;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Services;
using BTCPayServer.Plugins.USDt.Services.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Configuration;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBXplorer;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BTCPayServer.Plugins.USDt.Tests;

[Trait("Fast", "Fast")]
public class AddressPoolTests
{
    private const string EvmAddress = "0x742d35cc6634c0532925a3b844bc454e4438f44e";
    private const string MixedCaseEvmAddress = "0x742d35Cc6634C0532925a3b844Bc454e4438f44e";
    private const string TronAddress = "TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs";

    [Theory]
    [InlineData(true, "invalid")]
    [InlineData(false, "invalid")]
    [InlineData(true, "duplicate")]
    [InlineData(false, "duplicate")]
    [InlineData(true, "mixed-case-duplicate")]
    [InlineData(true, "null-pool")]
    [InlineData(false, "null-pool")]
    [InlineData(true, "null-entry")]
    [InlineData(false, "null-entry")]
    [InlineData(true, "blank-entry")]
    [InlineData(false, "blank-entry")]
    [InlineData(false, "wrong-length")]
    public async Task GreenfieldRejectsInvalidAddressPools(bool evm, string scenario)
    {
        var address = evm ? EvmAddress : TronAddress;
        JToken addresses = scenario switch
        {
            "null-pool" => JValue.CreateNull(),
            "null-entry" => new JArray(address, JValue.CreateNull()),
            "blank-entry" => new JArray(address, " "),
            "invalid" => new JArray(address, "invalid-address"),
            "mixed-case-duplicate" => new JArray(address, MixedCaseEvmAddress),
            "wrong-length" => new JArray(new Base58CheckEncoder().EncodeData([0x41])),
            _ => new JArray(address, address)
        };
        var context = ValidationContext(addresses);
        await Handler(evm).ValidatePaymentMethodConfig(context);
        Assert.False(context.ModelState.IsValid);
        Assert.NotEmpty(context.ModelState[nameof(USDtPaymentMethodConfig.Addresses)]!.Errors);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GreenfieldNormalizesValidAddressesAndAllowsClearingPool(bool evm)
    {
        var handler = Handler(evm);
        var context = ValidationContext(new JArray(" " + (evm ? MixedCaseEvmAddress : TronAddress) + " "));
        await handler.ValidatePaymentMethodConfig(context);
        Assert.True(context.ModelState.IsValid);
        var config = Assert.IsAssignableFrom<USDtPaymentMethodConfig>(handler.ParsePaymentMethodConfig(context.Config));
        Assert.Equal(new[] { evm ? EvmAddress : TronAddress }, config.Addresses);
        Assert.True(config.Activated);

        var cleared = new PaymentMethodConfigValidationContext(null!, new ModelStateDictionary(),
            new JObject { ["addresses"] = new JArray() }, new ClaimsPrincipal(), context.Config);
        await handler.ValidatePaymentMethodConfig(cleared);
        Assert.True(cleared.ModelState.IsValid);
        var empty = Assert.IsAssignableFrom<USDtPaymentMethodConfig>(handler.ParsePaymentMethodConfig(cleared.Config));
        Assert.Empty(empty.Addresses);
        Assert.True(empty.Activated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(22)]
    public void TronRejectsChecksumValidAddressesWithWrongPayloadLength(int length)
    {
        var payload = new byte[length];
        if (length > 0) payload[0] = 0x41;
        var address = new Base58CheckEncoder().EncodeData(payload);
        Assert.False(TronUSDtAddressHelper.IsValid(address));
        Assert.Throws<FormatException>(() => TronUSDtAddressHelper.Base58ToHex(address));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void TronRejectsMissingAddresses(string? address)
    {
        Assert.False(TronUSDtAddressHelper.IsValid(address!));
    }

    [Fact]
    public async Task EvmReservationMatchesBothAddressCasings()
    {
        var paymentMethodId = new PaymentMethodId("USDT-ETHEREUM");
        foreach (var reserved in new[] { EvmAddress, MixedCaseEvmAddress })
        {
            var invoice = new InvoiceEntity
            {
                Id = "reserved", Status = InvoiceStatus.New, MonitoringExpiration = DateTimeOffset.UtcNow.AddHours(1),
                Currency = "USD", Price = 1m
            };
            invoice.SetPaymentPrompt(paymentMethodId, new PaymentPrompt
                { Currency = "USDt", Destination = reserved, Divisibility = 6 });
            var provider = new USDtTrackedInvoiceProvider(new ReservedInvoice(invoice), TimeProvider.System);
            foreach (var configured in new[] { EvmAddress, MixedCaseEvmAddress })
            {
                var config = new EVMUSDtPaymentMethodConfig { Addresses = [configured] };
                Assert.Null(await config.GetOneNotReservedAddress(paymentMethodId, provider));
                config.Addresses = [configured, "0x1111111111111111111111111111111111111111"];
                Assert.Equal(config.Addresses[1], await config.GetOneNotReservedAddress(paymentMethodId, provider));
            }
        }
    }

    private static PaymentMethodConfigValidationContext ValidationContext(JToken addresses) =>
        new(null!, new ModelStateDictionary(), new JObject { ["addresses"] = addresses }, new ClaimsPrincipal(), null);

    private static IPaymentMethodHandler Handler(bool evm)
    {
        var network = new NBXplorerNetworkProvider(ChainName.Mainnet);
        var settings = new ConfigurationBuilder().Build();
        return evm
            ? new EVMUSDtPaymentMethodHandler(USDtConfigurationProvider.GetEVMUSDtLikeDefaultConfigurationItems(network, settings)
                [new PaymentMethodId("USDT-ETHEREUM")], null!, null!, null!)
            : new TronUSDtLikePaymentMethodHandler(USDtConfigurationProvider.GetTronUSDtLikeDefaultConfigurationItem(network, settings),
                null!, null!, null!);
    }

    private sealed class ReservedInvoice(InvoiceEntity invoice) : IUSDtInvoiceSource
    {
        public Task<InvoiceEntity[]> GetMonitoredInvoices(PaymentMethodId id, CancellationToken ct) => Task.FromResult(new[] { invoice });
        public Task<InvoiceEntity[]> GetExpiredInvoicesSince(DateTimeOffset since, CancellationToken ct) => Task.FromResult<InvoiceEntity[]>([]);
        public Task<InvoiceEntity[]> GetInvoices(string[] ids, CancellationToken ct) => Task.FromResult(new[] { invoice });
    }
}
