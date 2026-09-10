using System.Net;
using System.Numerics;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.EVM;
using BTCPayServer.Plugins.USDt.Services;
using BTCPayServer.Plugins.USDt.Services.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NBitcoin;
using NBXplorer;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BTCPayServer.Plugins.USDt.Tests;

public class USDtTransferListenerTests
{
    private const string Destination = "0x1111111111111111111111111111111111111111";
    private const string OtherDestination = "0x2222222222222222222222222222222222222222";
    private static readonly string TransactionHash = "0x" + new string('a', 64);
    private static readonly string LegacyId = $"{TransactionHash[2..]}-{new HexBigInteger(3)}";

    [Theory]
    [InlineData("ETHEREUM")]
    [InlineData("POLYGON")]
    [InlineData("BSC")]
    [InlineData("TRON")]
    public async Task ExpiredReplayKeepsSettledLegacyIdentity(string chain)
    {
        using var fixture = new Fixture(chain);
        var settled = fixture.Payment(LegacyId, PaymentStatus.Settled);
        var pending = fixture.Payment("other-transaction-4-log-9", PaymentStatus.Processing);
        fixture.TrackExpiredInvoice([settled, pending], [pending]);
        fixture.Rpc.Logs.Add(fixture.Log(7));

        // BTCPay's monitored projection for expired invoices omits settled payments,
        // even when the bootstrap query initially supplied a complete invoice.
        var monitored = Assert.Single(await fixture.Tracked.GetTrackedInvoices(
            fixture.PaymentMethodId, TestContext.Current.CancellationToken));
        Assert.Equal(pending.Id, Assert.Single(monitored.GetPayments(false)).Id);

        Assert.Equal(LegacyId, Assert.Single(await fixture.ReadIds([monitored])));
        Assert.Equal(new[] { "expired" }, Assert.Single(fixture.Source.HistoryQueries));
    }

    [Theory]
    [InlineData("ETHEREUM")]
    [InlineData("TRON")]
    public async Task ExpiredReplayKeepsSettledIndexedIdentityCasing(string chain)
    {
        using var fixture = new Fixture(chain);
        var storedId = $"{LegacyId}-log-7".ToUpperInvariant();
        var settled = fixture.Payment(storedId, PaymentStatus.Settled);
        var pending = fixture.Payment("other-transaction-4-log-9", PaymentStatus.Processing);
        fixture.TrackExpiredInvoice([settled, pending], [pending]);
        fixture.Rpc.Logs.Add(fixture.Log(7));

        Assert.Equal(storedId, Assert.Single(await fixture.ReadIds()));
    }

    [Theory]
    [InlineData("ETHEREUM")]
    [InlineData("TRON")]
    public async Task EqualAmountReplayDoesNotReassignASettledIndexedLog(string chain)
    {
        using var fixture = new Fixture(chain);
        var settled = fixture.Payment($"{LegacyId}-log-7", PaymentStatus.Settled);
        var pending = fixture.Payment(LegacyId, PaymentStatus.Processing);
        fixture.TrackExpiredInvoice([settled, pending], [pending]);
        fixture.Rpc.Logs.Add(fixture.Log(7));
        fixture.Rpc.Logs.Add(fixture.Log(8));

        Assert.Equal(new[] { settled.Id, pending.Id }, await fixture.ReadIds());
    }

    [Theory]
    [InlineData("ETHEREUM")]
    [InlineData("POLYGON")]
    [InlineData("BSC")]
    [InlineData("TRON")]
    public async Task NewTransfersOnlyLoadTouchedInvoices(string chain)
    {
        using var fixture = new Fixture(chain);
        fixture.TrackExpiredInvoice([], []);
        var unrelated = fixture.Invoice("unrelated", [], OtherDestination);
        fixture.Source.MonitoredInvoices = [..fixture.Source.MonitoredInvoices, unrelated];
        fixture.Rpc.Logs.Add(fixture.Log(7));
        fixture.Rpc.Logs.Add(fixture.Log(8));

        Assert.Equal(new[] { $"{LegacyId}-log-7", $"{LegacyId}-log-8" }, await fixture.ReadIds());
        Assert.Equal(new[] { "expired" }, Assert.Single(fixture.Source.HistoryQueries));
    }

    [Theory]
    [InlineData("ETHEREUM", "empty")]
    [InlineData("ETHEREUM", "removed")]
    [InlineData("ETHEREUM", "untracked")]
    [InlineData("TRON", "empty")]
    [InlineData("TRON", "removed")]
    [InlineData("TRON", "untracked")]
    public async Task BlocksWithoutRelevantLogsDoNotLoadPaymentHistory(string chain, string scenario)
    {
        using var fixture = new Fixture(chain);
        fixture.TrackExpiredInvoice([], []);
        if (scenario == "removed")
            fixture.Rpc.Logs.Add(fixture.Log(7, removed: true));
        else if (scenario == "untracked")
            fixture.Rpc.Logs.Add(fixture.Log(7, destination: OtherDestination));

        Assert.Empty(await fixture.ReadIds());
        Assert.Empty(fixture.Source.HistoryQueries);
    }

    [Theory]
    [InlineData("ETHEREUM")]
    [InlineData("TRON")]
    public async Task MissingPaymentHistoryStopsReplay(string chain)
    {
        using var fixture = new Fixture(chain);
        fixture.TrackExpiredInvoice([], []);
        fixture.Source.StoredInvoices.Clear();
        fixture.Rpc.Logs.Add(fixture.Log(7));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ReadIds());
        Assert.Contains("expired", error.Message);
    }

    [Theory]
    [InlineData("ETHEREUM")]
    [InlineData("TRON")]
    public async Task FailedPaymentHistoryLookupStopsReplay(string chain)
    {
        using var fixture = new Fixture(chain);
        fixture.TrackExpiredInvoice([], []);
        fixture.Source.HistoryError = new IOException("History lookup failed");
        fixture.Rpc.Logs.Add(fixture.Log(7));

        var error = await Assert.ThrowsAsync<IOException>(() => fixture.ReadIds());
        Assert.Same(fixture.Source.HistoryError, error);
    }

    [Theory]
    [InlineData("ETHEREUM")]
    [InlineData("POLYGON")]
    [InlineData("BSC")]
    [InlineData("TRON")]
    public async Task TransfersToDifferentInvoicesKeepIdsWhenOneStopsTracking(string chain)
    {
        using var fixture = new Fixture(chain);
        fixture.TrackExpiredInvoice([], []);
        var other = fixture.Invoice("other", [], OtherDestination);
        fixture.Source.StoredInvoices[other.Id] = other;
        fixture.Source.MonitoredInvoices = [..fixture.Source.MonitoredInvoices, other];
        fixture.Rpc.Logs.Add(fixture.Log(7));
        fixture.Rpc.Logs.Add(fixture.Log(8, destination: OtherDestination, value: 2));

        var initial = await fixture.ReadTransfers();
        Assert.Equal(2, initial.Select(transfer => transfer.Id).Distinct().Count());
        var previous = Assert.Single(initial, transfer => transfer.To == fixture.StoredAddress(OtherDestination));
        other = fixture.Invoice(other.Id,
            [fixture.Payment(previous.Id, PaymentStatus.Processing, OtherDestination, 2m)], OtherDestination);
        fixture.Source.StoredInvoices[other.Id] = other;
        fixture.Source.HistoryQueries.Clear();

        Assert.Equal(previous, Assert.Single(await fixture.ReadTransfers([other])));
        Assert.Equal(new[] { "other" }, Assert.Single(fixture.Source.HistoryQueries));
    }

    [Fact]
    public async Task TronRepeatedLogsPreserveBase58AddressesAndAmounts()
    {
        using var fixture = new Fixture("TRON");
        fixture.TrackExpiredInvoice([], []);
        fixture.Rpc.Logs.Add(fixture.Log(8, value: 2));
        fixture.Rpc.Logs.Add(fixture.Log(7));
        fixture.Rpc.Logs.Add(fixture.Log(7));

        var transfers = await fixture.ReadTransfers();
        Assert.Equal(new[] { $"{LegacyId}-log-7", $"{LegacyId}-log-8" }, transfers.Select(transfer => transfer.Id));
        Assert.Equal(3 * fixture.Units, transfers.Aggregate(BigInteger.Zero, (sum, transfer) => sum + transfer.Amount));
        Assert.All(transfers, transfer =>
        {
            Assert.Equal(fixture.StoredAddress(Destination), transfer.From);
            Assert.Equal(fixture.StoredAddress(Destination), transfer.To);
            Assert.Equal(transfer.To.ToLowerInvariant(), transfer.DestinationKey);
        });
    }

    [Fact]
    public async Task TronReplayKeepsTheLegacyRecipientWhenRpcOrderChanges()
    {
        using var fixture = new Fixture("TRON");
        fixture.TrackExpiredInvoice([], []);
        var legacy = fixture.Payment(LegacyId, PaymentStatus.Settled, OtherDestination, 2m);
        var other = fixture.Invoice("other", [legacy], OtherDestination);
        fixture.Source.StoredInvoices[other.Id] = other;
        fixture.Source.ExpiredInvoices = [..fixture.Source.ExpiredInvoices, other];
        fixture.Rpc.Logs.Add(fixture.Log(8, destination: OtherDestination, value: 2));
        fixture.Rpc.Logs.Add(fixture.Log(7));

        var transfers = await fixture.ReadTransfers();
        Assert.Equal(2, transfers.Length);
        var historical = Assert.Single(transfers, transfer => transfer.Id == LegacyId);
        Assert.Equal(fixture.StoredAddress(OtherDestination), historical.To);
        Assert.Equal(2 * fixture.Units, historical.Amount);
        Assert.Equal($"{LegacyId}-log-7", Assert.Single(transfers, transfer => transfer.To == fixture.StoredAddress(Destination)).Id);
    }

    [Fact]
    public async Task TronMissingLogIndexStopsProcessing()
    {
        using var fixture = new Fixture("TRON");
        fixture.TrackExpiredInvoice([], []);
        var log = fixture.Log(7);
        log["logIndex"] = null;
        fixture.Rpc.Logs.Add(log);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ReadIds());
    }

    [Fact]
    public async Task TronIgnoresLogsFromOtherContracts()
    {
        using var fixture = new Fixture("TRON");
        fixture.TrackExpiredInvoice([], []);
        var log = fixture.Log(7);
        log["address"] = OtherDestination;
        fixture.Rpc.Logs.Add(log);

        Assert.Empty(await fixture.ReadIds());
        Assert.Empty(fixture.Source.HistoryQueries);
    }

    [Fact]
    public async Task TronKeepsOneContractQueryForManyTrackedInvoices()
    {
        using var fixture = new Fixture("TRON");
        fixture.TrackExpiredInvoice([], []);
        fixture.Source.MonitoredInvoices = [..fixture.Source.MonitoredInvoices,
            ..Enumerable.Range(1, 25).Select(index => fixture.Invoice($"other-{index}", [], "0x" + index.ToString("x").PadLeft(40, '0')))];
        fixture.Rpc.Logs.Add(fixture.Log(7));

        Assert.Single(await fixture.ReadIds());
        var request = Assert.Single(fixture.Rpc.Requests);
        var filter = request["params"]![0]!;
        Assert.Equal("0x64", filter.Value<string>("fromBlock"));
        Assert.Equal("0x64", filter.Value<string>("toBlock"));
        Assert.Single((JArray)filter["topics"]!);
        Assert.Contains(fixture.Log(7).Value<string>("address")!, filter["address"]!.ToString());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly USDtPluginConfigurationItem _configuration;
        private readonly IUSDtRpcConfigurationItem _rpcConfiguration;
        private readonly IPaymentMethodHandler _handler;
        private readonly EventAggregator _aggregator = new(null!);
        private readonly HttpClient _httpClient;
        private readonly Func<PaymentMethodId, InvoiceEntity[], CancellationToken, Task<Transfer[]>> _readTransfers;

        public Fixture(string chain)
        {
            _httpClient = new HttpClient(Rpc);
            Tracked = new USDtTrackedInvoiceProvider(Source, TimeProvider.System);
            var network = new NBXplorerNetworkProvider(ChainName.Mainnet);
            var appConfiguration = new ConfigurationBuilder().Build();
            if (chain == "TRON")
            {
                var item = USDtConfigurationProvider.GetTronUSDtLikeDefaultConfigurationItem(network, appConfiguration)
                    with { JsonRpcUri = new Uri("https://rpc.invalid") };
                _configuration = item;
                _rpcConfiguration = item;
                var configuration = new USDtPluginConfiguration { TronUSDtLikeConfigurationItems = new() { [PaymentMethodId] = item } };
                var rpcProvider = new TronUSDtRPCProvider(configuration, _aggregator, null!, new HttpClientFactory(_httpClient));
                _handler = new TronUSDtLikePaymentMethodHandler(item, rpcProvider, null!, Tracked);
                _readTransfers = new TestTronListener(rpcProvider, configuration, Tracked, new PaymentMethodHandlerDictionary([_handler])).ReadTransfers;
            }
            else
            {
                var item = USDtConfigurationProvider.GetEVMUSDtLikeDefaultConfigurationItems(network, appConfiguration)
                    .Values.Single(config => config.Chain == chain) with { JsonRpcUri = new Uri("https://rpc.invalid") };
                _configuration = item;
                _rpcConfiguration = item;
                var configuration = new USDtPluginConfiguration { EVMUSDtLikeConfigurationItems = new() { [PaymentMethodId] = item } };
                var rpcProvider = new EVMUSDtRPCProvider(configuration, _aggregator, null!, new HttpClientFactory(_httpClient));
                _handler = new EVMUSDtPaymentMethodHandler(item, rpcProvider, null!, Tracked);
                _readTransfers = new TestEvmListener(rpcProvider, configuration, Tracked, new PaymentMethodHandlerDictionary([_handler])).ReadTransfers;
            }
        }

        public PaymentMethodId PaymentMethodId => _configuration.GetPaymentMethodId();
        public BigInteger Units => BigInteger.Pow(10, _rpcConfiguration.Divisibility);
        public string StoredAddress(string hex) => _configuration.Chain == "TRON" ? TronUSDtAddressHelper.HexToBase58(hex) : hex;
        public InvoiceSource Source { get; } = new();
        public RpcHandler Rpc { get; } = new();
        public USDtTrackedInvoiceProvider Tracked { get; }

        public void TrackExpiredInvoice(List<PaymentEntity> completePayments, List<PaymentEntity> monitoredPayments)
        {
            var complete = Invoice("expired", completePayments);
            Source.ExpiredInvoices = [complete];
            Source.StoredInvoices[complete.Id] = complete;
            Source.MonitoredInvoices = [Invoice(complete.Id, monitoredPayments)];
        }

        public PaymentEntity Payment(string id, PaymentStatus status, string destination = Destination, decimal value = 1m)
        {
            var payment = new PaymentEntity
            {
                Id = id, Status = status, PaymentMethodId = PaymentMethodId, Value = value, Currency = _configuration.Currency
            };
            payment.SetDetails(_handler, new USDtPaymentData
            {
                TransactionId = id, From = StoredAddress(Destination), To = StoredAddress(destination), BlockHeight = 100,
                ConfirmationCount = status == PaymentStatus.Settled ? 20 : 0
            });
            return payment;
        }

        public InvoiceEntity Invoice(string id, List<PaymentEntity> payments, string destination = Destination)
        {
            var invoice = new InvoiceEntity
            {
                Id = id, Status = InvoiceStatus.Expired, MonitoringExpiration = DateTimeOffset.UtcNow.AddDays(1),
                Currency = "USD", Price = 10m
            };
            invoice.SetPaymentPrompt(PaymentMethodId, new PaymentPrompt
            {
                Destination = StoredAddress(destination), Currency = _configuration.Currency, Divisibility = _rpcConfiguration.Divisibility
            });
#pragma warning disable CS0618
            invoice.Payments = payments;
#pragma warning restore CS0618
            return invoice;
        }

        public JObject Log(int index, bool removed = false, string destination = Destination, int value = 1) => new()
        {
            ["address"] = _configuration.Chain == "TRON" ? TronUSDtAddressHelper.Base58ToHex(_rpcConfiguration.SmartContractAddress) : _rpcConfiguration.SmartContractAddress,
            ["blockNumber"] = "0x64", ["blockHash"] = "0x" + new string('b', 64),
            ["transactionHash"] = TransactionHash, ["transactionIndex"] = "0x3",
            ["logIndex"] = "0x" + index.ToString("x"), ["removed"] = removed,
            ["data"] = "0x" + (Units * value).ToString("x").PadLeft(64, '0'),
            ["topics"] = new JArray("0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef",
                "0x" + Destination[2..].PadLeft(64, '0'), "0x" + destination[2..].PadLeft(64, '0'))
        };

        public async Task<string[]> ReadIds(InvoiceEntity[]? invoices = null) =>
            (await ReadTransfers(invoices)).Select(transfer => transfer.Id).ToArray();

        public async Task<Transfer[]> ReadTransfers(InvoiceEntity[]? invoices = null)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            invoices ??= await Tracked.GetTrackedInvoices(PaymentMethodId, cancellationToken);
            return await _readTransfers(PaymentMethodId, invoices, cancellationToken);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            _aggregator.Dispose();
        }
    }

    private sealed class InvoiceSource : IUSDtInvoiceSource
    {
        public InvoiceEntity[] MonitoredInvoices { get; set; } = [];
        public InvoiceEntity[] ExpiredInvoices { get; set; } = [];
        public Dictionary<string, InvoiceEntity> StoredInvoices { get; } = new();
        public List<string[]> HistoryQueries { get; } = [];
        public Exception? HistoryError { get; set; }

        public Task<InvoiceEntity[]> GetMonitoredInvoices(PaymentMethodId pmi, CancellationToken ct) => Task.FromResult(MonitoredInvoices);
        public Task<InvoiceEntity[]> GetExpiredInvoicesSince(DateTimeOffset since, CancellationToken ct) => Task.FromResult(ExpiredInvoices);
        public Task<InvoiceEntity[]> GetInvoices(string[] ids, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            HistoryQueries.Add(ids);
            if (HistoryError is not null)
                throw HistoryError;
            return Task.FromResult(ids.Where(StoredInvoices.ContainsKey).Select(id => StoredInvoices[id]).ToArray());
        }
    }

    private sealed record Transfer(string Id, string DestinationKey, string From, string To, BigInteger Amount);

    private sealed class TestEvmListener(EVMUSDtRPCProvider rpc, USDtPluginConfiguration config,
        USDtTrackedInvoiceProvider tracked, PaymentMethodHandlerDictionary handlers)
        : EVMUSDtListener(null!, null!, rpc, config, null!, tracked, NullLogger<EVMUSDtListener>.Instance, handlers, null!)
    {
        public async Task<Transfer[]> ReadTransfers(PaymentMethodId pmi, InvoiceEntity[] invoices, CancellationToken cancellationToken) =>
            (await GetTransfersAsync(pmi, new BlockWithTransactions { Number = new HexBigInteger(100) },
                invoices.ToDictionary(invoice => invoice.GetPaymentPrompt(pmi)!.Destination.ToLowerInvariant()), cancellationToken))
            .Select(match => new Transfer(match.TransactionId, match.DestinationKey, match.From, match.To, match.TotalAmount)).ToArray();
    }

    private sealed class TestTronListener(TronUSDtRPCProvider rpc, USDtPluginConfiguration config,
        USDtTrackedInvoiceProvider tracked, PaymentMethodHandlerDictionary handlers)
        : TronUSDtListener(null!, null!, rpc, config, null!, tracked, NullLogger<TronUSDtListener>.Instance, handlers, null!)
    {
        public async Task<Transfer[]> ReadTransfers(PaymentMethodId pmi, InvoiceEntity[] invoices, CancellationToken cancellationToken) =>
            (await GetTransfersAsync(pmi, new BlockWithTransactions { Number = new HexBigInteger(100) },
                invoices.ToDictionary(invoice => invoice.GetPaymentPrompt(pmi)!.Destination.ToLowerInvariant()), cancellationToken))
            .Select(match => new Transfer(match.TransactionId, match.DestinationKey, match.From, match.To, match.TotalAmount)).ToArray();
    }

    private sealed class HttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RpcHandler : HttpMessageHandler
    {
        public JArray Logs { get; } = [];
        public List<JObject> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var input = JObject.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("eth_getLogs", input.Value<string>("method"));
            Requests.Add(input);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(new JObject { ["jsonrpc"] = "2.0", ["id"] = input["id"], ["result"] = Logs }.ToString())
            };
        }
    }
}
