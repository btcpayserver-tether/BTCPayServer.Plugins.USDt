using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.Ethereum;
using BTCPayServer.Plugins.USDt.Services.Events;
using BTCPayServer.Services;
using NBitcoin;
using Nethereum.JsonRpc.Client;
using Nethereum.StandardTokenEIP20;
using Nethereum.Web3;

namespace BTCPayServer.Plugins.USDt.Services;

public class EthErc20RPCProvider
{
    private readonly EventAggregator _eventAggregator;
    private readonly IEventAggregatorSubscription _eventAggregatorSubscription;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SettingsRepository _settingsRepository;

    private readonly USDtPluginConfiguration _usdtPluginConfiguration;
    private ImmutableDictionary<PaymentMethodId, RpcClient>? _walletRpcClients;

    public EthErc20RPCProvider(USDtPluginConfiguration usdtPluginConfiguration,
        EventAggregator eventAggregator,
        SettingsRepository settingsRepository,
        IHttpClientFactory httpClientFactory)
    {
        _usdtPluginConfiguration = usdtPluginConfiguration;
        _eventAggregator = eventAggregator;
        _settingsRepository = settingsRepository;
        _httpClientFactory = httpClientFactory;

        _eventAggregatorSubscription = _eventAggregator.Subscribe<EthErc20SettingsChanged>(_ => LoadClientsFromConfiguration());
        LoadClientsFromConfiguration();
    }

    public ConcurrentDictionary<PaymentMethodId, Erc20LikeSummary> Summaries { get; } = new();

    private void LoadClientsFromConfiguration()
    {
        lock (this)
        {
            _walletRpcClients = _usdtPluginConfiguration.EthereumErc20LikeConfigurationItems.ToImmutableDictionary(
                pair => pair.Key,
                pair =>
                {
                    var httpClient = _httpClientFactory.CreateClient();
                    var rpcClient = new RpcClient(pair.Value.JsonRpcUri, httpClient);
                    return rpcClient;
                });
        }
    }

    public Web3 GetWeb3Client(PaymentMethodId pmi)
    {
        lock (this)
        {
            return new Web3(_walletRpcClients![pmi]);
        }
    }

    public bool IsAvailable(PaymentMethodId pmi)
    {
        return Summaries.ContainsKey(pmi) && IsAvailable(Summaries[pmi]);
    }

    private static bool IsAvailable(Erc20LikeSummary summary)
    {
        return summary is { Synced: true, RpcAvailable: true };
    }

    public async Task<(string, decimal?)[]> GetBalances(PaymentMethodId pmi, IEnumerable<string> addresses)
    {
        var configuration = _usdtPluginConfiguration.EthereumErc20LikeConfigurationItems[pmi];
        var tokenService = new StandardTokenService(GetWeb3Client(pmi), configuration.SmartContractAddress);

        List<(string, decimal?)> results = new();
        foreach (var address in addresses)
        {
            try
            {
                var balanceResult = await tokenService.BalanceOfQueryAsync(address);
                var divisor = BigInteger.Pow(10, configuration.Divisibility);
                var quotient = balanceResult / divisor;
                var remainder = balanceResult % divisor;
                var fractionalPart = (decimal)remainder / (decimal)divisor;
                results.Add((address, (decimal)quotient + fractionalPart));
            }
            catch (Exception)
            {
                results.Add((address, null));
            }
        }

        return results.ToArray();
    }

    public async Task UpdateSummary(PaymentMethodId pmi)
    {
        if (!_walletRpcClients!.TryGetValue(pmi, out _)) return;

        var configuration = _usdtPluginConfiguration.EthereumErc20LikeConfigurationItems[pmi];
        var listenerState = await _settingsRepository.GetSettingAsync<EthErc20ListenerState>(ListenerStateSettingKey(configuration));
        if (listenerState == null) return;

        var summary = new Erc20LikeSummary();
        try
        {
            summary.LatestBlockScanned = listenerState.LastBlockHeight;

            var web3Client = GetWeb3Client(pmi);
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

            summary.Synced = summary.HighestBlockOnChain - listenerState.LastBlockHeight < (int)(TimeSpan.FromMinutes(10).TotalSeconds / 12.0);

            summary.UpdatedAt = DateTime.UtcNow;
            summary.RpcAvailable = true;
        }
        catch
        {
            summary.RpcAvailable = false;
        }

        var changed = !Summaries.ContainsKey(pmi) || IsAvailable(pmi) != IsAvailable(summary);

        Summaries.AddOrReplace(pmi, summary);
        if (changed)
            _eventAggregator.Publish(new EthErc20DaemonStateChanged { Summary = summary, PaymentMethodId = pmi });
    }

    public static string ListenerStateSettingKey(EthErc20LikeConfigurationItem config)
    {
        return $"{config.GetSettingPrefix()}_LISTENER_STATE";
    }

    public class Erc20LikeSummary
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
