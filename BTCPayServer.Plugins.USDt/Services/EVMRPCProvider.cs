using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Services;
using NBitcoin;
using Nethereum.JsonRpc.Client;
using Nethereum.StandardTokenEIP20;
using Nethereum.Web3;

namespace BTCPayServer.Plugins.USDt.Services;

/// <summary>
/// Base class for EVM-based RPC providers (Tron, Ethereum, etc.)
/// Handles Web3 client management, summary updates, and balance queries.
/// </summary>
/// <typeparam name="TConfig">The configuration item type</typeparam>
/// <typeparam name="TSettingsChanged">The settings changed event type</typeparam>
public abstract class EVMRPCProvider<TConfig, TSettingsChanged>
    where TConfig : USDtPluginConfigurationItem, IEVMConfigurationItem
{
    private readonly EventAggregator _eventAggregator;
    private readonly IEventAggregatorSubscription _eventAggregatorSubscription;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SettingsRepository _settingsRepository;
    private ImmutableDictionary<PaymentMethodId, RpcClient>? _walletRpcClients;

    public ConcurrentDictionary<PaymentMethodId, EVMSummary> Summaries { get; } = new();

    protected EVMRPCProvider(
        EventAggregator eventAggregator,
        SettingsRepository settingsRepository,
        IHttpClientFactory httpClientFactory)
    {
        _eventAggregator = eventAggregator;
        _settingsRepository = settingsRepository;
        _httpClientFactory = httpClientFactory;

        _eventAggregatorSubscription =
            _eventAggregator.Subscribe<TSettingsChanged>(_ => LoadClientsFromConfiguration());
        LoadClientsFromConfiguration();
    }

    /// <summary>
    /// Get the configuration items dictionary for this provider
    /// </summary>
    protected abstract IDictionary<PaymentMethodId, TConfig> GetConfigurationItems();

    /// <summary>
    /// Convert address from chain-specific format to hex for RPC calls
    /// </summary>
    protected abstract string AddressToHex(string address);

    /// <summary>
    /// Convert address from hex to chain-specific format
    /// </summary>
    protected abstract string HexToAddress(string hex);

    /// <summary>
    /// Get block time in seconds for calculating sync thresholds
    /// </summary>
    protected abstract double GetBlockTimeSeconds();

    /// <summary>
    /// Publish daemon state changed event
    /// </summary>
    protected abstract void PublishDaemonStateChanged(PaymentMethodId pmi, EVMSummary summary);

    private void LoadClientsFromConfiguration()
    {
        lock (this)
        {
            _walletRpcClients = GetConfigurationItems().ToImmutableDictionary(
                pair => pair.Key,
                pair =>
                {
                    var httpClient = _httpClientFactory.CreateClient($"{pair.Key}client");
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

    private static bool IsAvailable(EVMSummary summary)
    {
        return summary is { Synced: true, RpcAvailable: true };
    }

    public async Task<(string, decimal?)[]> GetBalances(PaymentMethodId pmi, IEnumerable<string> addresses)
    {
        var configuration = GetConfigurationItems()[pmi];
        var contractAddress = AddressToHex(configuration.SmartContractAddress);
        var tokenService = new StandardTokenService(GetWeb3Client(pmi), contractAddress);

        List<(string, decimal?)> results = [];
        foreach (var address in addresses)
            try
            {
                var hexAddress = AddressToHex(address);
                var balanceResult = await tokenService.BalanceOfQueryAsync(hexAddress);
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

        return results.ToArray();
    }

    public async Task UpdateSummary(PaymentMethodId pmi)
    {
        if (!_walletRpcClients!.TryGetValue(pmi, out _)) return;

        var configuration = GetConfigurationItems()[pmi];
        var listenerState =
            await _settingsRepository.GetSettingAsync<EVMBasedListenerState>(ListenerStateSettingKey(configuration));
        if (listenerState == null) return;

        var summary = new EVMSummary();
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

            // Calculate sync threshold based on block time
            var blocksPerTenMinutes = (int)(TimeSpan.FromMinutes(10).TotalSeconds / GetBlockTimeSeconds());
            summary.Synced = summary.HighestBlockOnChain - listenerState.LastBlockHeight < blocksPerTenMinutes;

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
            PublishDaemonStateChanged(pmi, summary);
    }

    public static string ListenerStateSettingKey(TConfig config)
    {
        return $"{config.GetSettingPrefix()}_LISTENER_STATE";
    }
}
