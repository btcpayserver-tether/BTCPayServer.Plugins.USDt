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

public class EvmReplayHistoryTests
{
    private const string Destination = "0x1111111111111111111111111111111111111111";
    private const string OtherDestination = "0x2222222222222222222222222222222222222222";
    private static readonly string TransactionHash = "0x" + new string('a', 64);
    private static readonly string LegacyId = $"{TransactionHash[2..]}-{new HexBigInteger(3)}";

    [Theory]
    [InlineData(1)]
    [InlineData(137)]
    [InlineData(56)]
    public async Task ExpiredReplayKeepsSettledLegacyIdentity(int chainId)
    {
        using var fixture = new Fixture(chainId);
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

    [Fact]
    public async Task ExpiredReplayKeepsSettledIndexedIdentityCasing()
    {
        using var fixture = new Fixture(1);
        var storedId = $"{LegacyId}-log-7".ToUpperInvariant();
        var settled = fixture.Payment(storedId, PaymentStatus.Settled);
        var pending = fixture.Payment("other-transaction-4-log-9", PaymentStatus.Processing);
        fixture.TrackExpiredInvoice([settled, pending], [pending]);
        fixture.Rpc.Logs.Add(fixture.Log(7));

        Assert.Equal(storedId, Assert.Single(await fixture.ReadIds()));
    }

    [Fact]
    public async Task EqualAmountReplayDoesNotReassignASettledIndexedLog()
    {
        using var fixture = new Fixture(1);
        var settled = fixture.Payment($"{LegacyId}-log-7", PaymentStatus.Settled);
        var pending = fixture.Payment(LegacyId, PaymentStatus.Processing);
        fixture.TrackExpiredInvoice([settled, pending], [pending]);
        fixture.Rpc.Logs.Add(fixture.Log(7));
        fixture.Rpc.Logs.Add(fixture.Log(8));

        Assert.Equal(new[] { settled.Id, pending.Id }, await fixture.ReadIds());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(137)]
    [InlineData(56)]
    public async Task NewTransfersOnlyLoadTouchedInvoices(int chainId)
    {
        using var fixture = new Fixture(chainId);
        fixture.TrackExpiredInvoice([], []);
        var unrelated = fixture.Invoice("unrelated", [], OtherDestination);
        fixture.Source.MonitoredInvoices = [..fixture.Source.MonitoredInvoices, unrelated];
        fixture.Rpc.Logs.Add(fixture.Log(7));
        fixture.Rpc.Logs.Add(fixture.Log(8));

        Assert.Equal(new[] { $"{LegacyId}-log-7", $"{LegacyId}-log-8" }, await fixture.ReadIds());
        Assert.Equal(new[] { "expired" }, Assert.Single(fixture.Source.HistoryQueries));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("removed")]
    [InlineData("untracked")]
    public async Task BlocksWithoutRelevantLogsDoNotLoadPaymentHistory(string scenario)
    {
        using var fixture = new Fixture(1);
        fixture.TrackExpiredInvoice([], []);
        if (scenario == "removed")
            fixture.Rpc.Logs.Add(fixture.Log(7, removed: true));
        else if (scenario == "untracked")
            fixture.Rpc.Logs.Add(fixture.Log(7, destination: OtherDestination));

        Assert.Empty(await fixture.ReadIds());
        Assert.Empty(fixture.Source.HistoryQueries);
    }

    [Fact]
    public async Task MissingPaymentHistoryStopsReplay()
    {
        using var fixture = new Fixture(1);
        fixture.TrackExpiredInvoice([], []);
        fixture.Source.StoredInvoices.Clear();
        fixture.Rpc.Logs.Add(fixture.Log(7));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ReadIds());
        Assert.Contains("expired", error.Message);
    }

    [Fact]
    public async Task FailedPaymentHistoryLookupStopsReplay()
    {
        using var fixture = new Fixture(1);
        fixture.TrackExpiredInvoice([], []);
        fixture.Source.HistoryError = new IOException("History lookup failed");
        fixture.Rpc.Logs.Add(fixture.Log(7));

        var error = await Assert.ThrowsAsync<IOException>(() => fixture.ReadIds());
        Assert.Same(fixture.Source.HistoryError, error);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly EVMUSDtLikeConfigurationItem _configuration;
        private readonly EVMUSDtPaymentMethodHandler _handler;
        private readonly EventAggregator _aggregator = new(null!);
        private readonly HttpClient _httpClient;
        private readonly TestListener _listener;

        public Fixture(int chainId)
        {
            _configuration = USDtConfigurationProvider.GetEVMUSDtLikeDefaultConfigurationItems(
                new NBXplorerNetworkProvider(ChainName.Mainnet), new ConfigurationBuilder().Build())
                .Values.Single(config => config.ChainId == chainId) with { JsonRpcUri = new Uri("https://rpc.invalid") };
            var configuration = new USDtPluginConfiguration
            {
                EVMUSDtLikeConfigurationItems = new() { [PaymentMethodId] = _configuration }
            };
            _httpClient = new HttpClient(Rpc);
            var rpcProvider = new EVMUSDtRPCProvider(configuration, _aggregator, null!, new HttpClientFactory(_httpClient));
            Tracked = new USDtTrackedInvoiceProvider(Source, TimeProvider.System);
            _handler = new EVMUSDtPaymentMethodHandler(_configuration, rpcProvider, null!, Tracked);
            _listener = new TestListener(rpcProvider, configuration, Tracked, new PaymentMethodHandlerDictionary([_handler]));
        }

        public PaymentMethodId PaymentMethodId => _configuration.GetPaymentMethodId();
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

        public PaymentEntity Payment(string id, PaymentStatus status)
        {
            var payment = new PaymentEntity
            {
                Id = id, Status = status, PaymentMethodId = PaymentMethodId, Value = 1m, Currency = _configuration.Currency
            };
            payment.SetDetails(_handler, new EVMUSDtPaymentData
            {
                TransactionId = id, From = Destination, To = Destination, BlockHeight = 100,
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
                Destination = destination, Currency = _configuration.Currency, Divisibility = _configuration.Divisibility
            });
#pragma warning disable CS0618
            invoice.Payments = payments;
#pragma warning restore CS0618
            return invoice;
        }

        public JObject Log(int index, bool removed = false, string destination = Destination) => new()
        {
            ["address"] = _configuration.SmartContractAddress,
            ["blockNumber"] = "0x64", ["blockHash"] = "0x" + new string('b', 64),
            ["transactionHash"] = TransactionHash, ["transactionIndex"] = "0x3",
            ["logIndex"] = "0x" + index.ToString("x"), ["removed"] = removed,
            ["data"] = "0x" + BigInteger.Pow(10, _configuration.Divisibility).ToString("x").PadLeft(64, '0'),
            ["topics"] = new JArray("0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef",
                "0x" + Destination[2..].PadLeft(64, '0'), "0x" + destination[2..].PadLeft(64, '0'))
        };

        public async Task<string[]> ReadIds(InvoiceEntity[]? invoices = null)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            invoices ??= await Tracked.GetTrackedInvoices(PaymentMethodId, cancellationToken);
            return await _listener.ReadIds(PaymentMethodId, invoices, cancellationToken);
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

    private sealed class TestListener(EVMUSDtRPCProvider rpc, USDtPluginConfiguration config,
        USDtTrackedInvoiceProvider tracked, PaymentMethodHandlerDictionary handlers)
        : EVMUSDtListener(null!, null!, rpc, config, null!, tracked, NullLogger<EVMUSDtListener>.Instance, handlers, null!)
    {
        public async Task<string[]> ReadIds(PaymentMethodId pmi, InvoiceEntity[] invoices, CancellationToken cancellationToken) =>
            (await GetTransfersAsync(pmi, new BlockWithTransactions { Number = new HexBigInteger(100) },
                invoices.ToDictionary(invoice => invoice.GetPaymentPrompt(pmi)!.Destination.ToLowerInvariant()), cancellationToken))
            .Select(match => match.TransactionId).ToArray();
    }

    private sealed class HttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RpcHandler : HttpMessageHandler
    {
        public JArray Logs { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var input = JObject.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("eth_getLogs", input.Value<string>("method"));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(new JObject { ["jsonrpc"] = "2.0", ["id"] = input["id"], ["result"] = Logs }.ToString())
            };
        }
    }
}
