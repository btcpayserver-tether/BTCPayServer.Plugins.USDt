using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using BTCPayServer.Plugins.TronUSDT.Configuration;
using BTCPayServer.Plugins.TronUSDT.Services.Events;
using BTCPayServer.Services;
using NBitcoin;
using Nethereum.JsonRpc.Client;
using Nethereum.StandardTokenEIP20;
using Nethereum.Web3;

namespace BTCPayServer.Plugins.TronUSDT.Services;

public class TronUSDTRPCProvider
{
    private readonly EventAggregator _eventAggregator;
    private readonly IEventAggregatorSubscription _eventAggregatorSubscription;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly SettingsRepository _settingsRepository;

    private readonly TronUSDTLikeConfiguration _tronUSDTLikeConfiguration;
    private ImmutableDictionary<string, RpcClient>? _walletRpcClients;

    public TronUSDTRPCProvider(TronUSDTLikeConfiguration tronUSDTLikeConfiguration,
        EventAggregator eventAggregator,
        SettingsRepository settingsRepository,
        BTCPayNetworkProvider networkProvider,
        IHttpClientFactory httpClientFactory)
    {
        _tronUSDTLikeConfiguration = tronUSDTLikeConfiguration;
        _eventAggregator = eventAggregator;
        _settingsRepository = settingsRepository;
        _networkProvider = networkProvider;
        _httpClientFactory = httpClientFactory;

        _eventAggregatorSubscription = _eventAggregator.Subscribe<TronUSDTSettingsChanged>(_ => LoadClientsFromConfiguration());
        LoadClientsFromConfiguration();
    }

    public ConcurrentDictionary<string, TronUSDTLikeSummary> Summaries { get; } = new();

    private void LoadClientsFromConfiguration()
    {
        lock (this)
        {
            _walletRpcClients = _tronUSDTLikeConfiguration.TronUSDTLikeConfigurationItems.ToImmutableDictionary(
                pair => pair.Key,
                pair =>
                {
                    var httpClient = _httpClientFactory.CreateClient($"{pair.Key}client");
                    var rpcClient = new RpcClient(pair.Value.JsonRpcUri, httpClient);
                    return rpcClient;
                });
        }
    }

    public Web3 GetWeb3Client(string cryptoCode)
    {
        lock (this)
        {
            return new Web3(_walletRpcClients[cryptoCode]);
        }
    }

    public bool IsAvailable(string cryptoCode)
    {
        cryptoCode = cryptoCode.ToUpperInvariant();
        return Summaries.ContainsKey(cryptoCode) && IsAvailable(Summaries[cryptoCode]);
    }

    private static bool IsAvailable(TronUSDTLikeSummary summary)
    {
        return summary is { Synced: true, RpcAvailable: true };
    }

    public async Task<(string, decimal?)[]> GetBalances(string cryptoCode, IEnumerable<string> addresses)
    {
        var configuration = _tronUSDTLikeConfiguration.TronUSDTLikeConfigurationItems[cryptoCode];

        var tokenService = new StandardTokenService(GetWeb3Client(cryptoCode),
            TronUSDTAddressHelper.Base58ToHex(configuration.SmartContractAddress));

        var hexAddresses = addresses.Select(TronUSDTAddressHelper.Base58ToHex);
        var divisibility = _networkProvider.GetNetwork(cryptoCode).Divisibility;

        List<(string, decimal?)> results = [];
        foreach (var address in hexAddresses)
        {
            var base58Address = TronUSDTAddressHelper.HexToBase58(address);
            try
            {
                var balanceResult = await tokenService.BalanceOfQueryAsync(address);
                var divisor = BigInteger.Pow(10, divisibility);
                var quotient = balanceResult / divisor;
                var remainder = balanceResult % divisor;
                var fractionalPart = (decimal)remainder / (decimal)divisor;
                results.Add((base58Address, (decimal)quotient + fractionalPart));
            }
            catch (Exception)
            {
                results.Add((base58Address, null));
            }
        }

        return results.ToArray();
    }

    public async Task UpdateSummary(string cryptoCode)
    {
        if (!_walletRpcClients.TryGetValue(cryptoCode.ToUpperInvariant(), out _)) return;

        var listenerState =
            await _settingsRepository.GetSettingAsync<TronUSDTListenerState>(ListenerStateSettingKey(cryptoCode));
        if (listenerState == null) return;

        var summary = new TronUSDTLikeSummary();
        try
        {
            summary.LatestBlockScanned = listenerState.LastBlockHeight;

            var web3Client = GetWeb3Client(cryptoCode);
            var syncingOutput = await web3Client.Eth.Syncing.SendRequestAsync();
            if (syncingOutput.IsSyncing)
            {
                summary.LatestBlockOnNode = syncingOutput.CurrentBlock.Value;
                summary.HighestBlockOnChain = syncingOutput.HighestBlock.Value;
                summary.Syncing = true;
            }
            else
            {
                var latestBlock = await web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync();
                summary.LatestBlockOnNode = latestBlock;
                summary.HighestBlockOnChain = latestBlock;
                summary.Syncing = false;
            }

            summary.Synced = summary.HighestBlockOnChain - listenerState.LastBlockHeight < (int)(TimeSpan.FromMinutes(10).TotalSeconds / 3.0);

            summary.UpdatedAt = DateTime.UtcNow;
            summary.RpcAvailable = true;
        }
        catch
        {
            summary.RpcAvailable = false;
        }

        var changed = !Summaries.ContainsKey(cryptoCode) || IsAvailable(cryptoCode) != IsAvailable(summary);

        Summaries.AddOrReplace(cryptoCode, summary);
        if (changed)
            _eventAggregator.Publish(new TronUSDTDaemonStateChanged { Summary = summary, CryptoCode = cryptoCode });
    }

    public static string ListenerStateSettingKey(string cryptoCode)
    {
        return "TRONUSDT_LISTENER_" + cryptoCode;
    }

    public class TronUSDTLikeSummary
    {
        public bool Synced { get; set; }
        public BigInteger LatestBlockOnNode { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool RpcAvailable { get; set; }
        public BigInteger HighestBlockOnChain { get; set; }
        public BigInteger LatestBlockScanned { get; set; }
        public bool Syncing { get; set; }
    }
}