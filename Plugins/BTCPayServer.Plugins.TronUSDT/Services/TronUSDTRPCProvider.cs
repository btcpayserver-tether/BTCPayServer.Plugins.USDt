using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using BTCPayServer.Plugins.TronUSDT.Configuration;
using BTCPayServer.Plugins.TronUSDT.Tron.TronUSDT;
using BTCPayServer.Services;
using NBitcoin;
using Nethereum.JsonRpc.Client;
using Nethereum.StandardTokenEIP20;
using Nethereum.Web3;

namespace BTCPayServer.Plugins.TronUSDT.Services;

public class TronUSDTListenerState
{
    public BigInteger LastBlockHeight { get; set; }
}

public class TronUSDTRPCProvider
{
    private readonly EventAggregator _eventAggregator;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly SettingsRepository _settingsRepository;

    private readonly TronUSDTLikeConfiguration _tronUSDTLikeConfiguration;
    public readonly ImmutableDictionary<string, RpcClient> WalletRpcClients;

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

        WalletRpcClients = _tronUSDTLikeConfiguration.TronUSDTLikeConfigurationItems.ToImmutableDictionary(
            pair => pair.Key,
            pair =>
            {
                var httpClient = httpClientFactory.CreateClient($"{pair.Key}client");
                //System.Net.WebRequest.DefaultWebProxy = new System.Net.WebProxy("127.0.0.1", 8888);
                //httpClient.DefaultRequestHeaders.Add("TRON-PRO-API-KEY", "a960115e-8f38-4893-9469-65c384b0a921");
                var rpcClient = new RpcClient(pair.Value.JsonRpcUri, httpClient);
                return rpcClient;
            });
    }

    public ConcurrentDictionary<string, TronUSDTLikeSummary> Summaries { get; } = new();

    public bool IsAvailable(string cryptoCode)
    {
        cryptoCode = cryptoCode.ToUpperInvariant();
        return Summaries.ContainsKey(cryptoCode) && IsAvailable(Summaries[cryptoCode]);
    }

    private bool IsAvailable(TronUSDTLikeSummary summary)
    {
        return summary.Synced &&
               summary.RpcAvailable;
    }

    public Web3 GetWeb3Client(string cryptoCode)
    {
        return new Web3(WalletRpcClients[cryptoCode]);
    }

    public Task<(string, decimal)[]> GetBalances(string cryptoCode, string[] addresses)
    {
        var configuration = _tronUSDTLikeConfiguration.TronUSDTLikeConfigurationItems[cryptoCode];

        var tokenService = new StandardTokenService(GetWeb3Client(cryptoCode),
            TronUSDTAddressHelper.Base58ToHex(configuration.SmartContractAddress));

        var tokens = addresses.Select(TronUSDTAddressHelper.Base58ToHex).Select(a =>
            (TronUSDTAddressHelper.HexToBase58(a), tokenService.BalanceOfQueryAsync(a).Result)).ToArray();
        var divisibility = _networkProvider.GetNetwork(cryptoCode).Divisibility;

        return Task.FromResult(tokens.Select(r =>
        {
            var divisor = BigInteger.Pow(10, divisibility);
            var quotient = r.Result / divisor;
            var remainder = r.Result % divisor;
            var fractionalPart = (decimal)remainder / (decimal)divisor;

            return (r.Item1, (decimal)quotient + fractionalPart);
        }).ToArray());
    }

    public async Task<TronUSDTLikeSummary> UpdateSummary(string cryptoCode)
    {
        if (!WalletRpcClients.TryGetValue(cryptoCode.ToUpperInvariant(), out var walletRpcClient)) return null;

        var listenerState =
            await _settingsRepository.GetSettingAsync<TronUSDTListenerState>(ListenerStateSettingKey(cryptoCode));
        if (listenerState == null) return null;

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
            _eventAggregator.Publish(new TronUSDTDaemonStateChange { Summary = summary, CryptoCode = cryptoCode });

        return summary;
    }

    public static string ListenerStateSettingKey(string cryptoCode)
    {
        return "TRONUSDT_LISTENER_" + cryptoCode;
    }


    public class TronUSDTDaemonStateChange
    {
        public string CryptoCode { get; set; }
        public TronUSDTLikeSummary Summary { get; set; }
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