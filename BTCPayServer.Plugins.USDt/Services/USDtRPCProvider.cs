using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Services.Events;
using BTCPayServer.Services;
using NBitcoin;
using Nethereum.JsonRpc.Client;
using Nethereum.StandardTokenEIP20;
using Nethereum.Web3;

namespace BTCPayServer.Plugins.USDt.Services;

public abstract class USDtRPCProvider<TConfigurationItem>(
    EventAggregator eventAggregator,
    SettingsRepository settingsRepository,
    IHttpClientFactory httpClientFactory)
    where TConfigurationItem : USDtPluginConfigurationItem, IUSDtRpcConfigurationItem
{
    private readonly EventAggregator _eventAggregator = eventAggregator;
    private readonly SettingsRepository _settingsRepository = settingsRepository;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private IEventAggregatorSubscription? _eventAggregatorSubscription;
    private ImmutableDictionary<PaymentMethodId, RpcClient>? _walletRpcClients;

    public ConcurrentDictionary<PaymentMethodId, USDtRpcSummary> Summaries { get; } = new();

    protected void Initialize()
    {
        _eventAggregatorSubscription = _eventAggregator.Subscribe<USDtSettingsChanged>(_ => LoadClientsFromConfiguration());
        LoadClientsFromConfiguration();
    }

    protected abstract IReadOnlyDictionary<PaymentMethodId, TConfigurationItem> GetConfigurations();
    protected abstract string GetListenerStateSettingKey(TConfigurationItem config);

    protected virtual HttpClient CreateHttpClient(PaymentMethodId paymentMethodId, TConfigurationItem config)
    {
        return _httpClientFactory.CreateClient();
    }

    protected virtual string NormalizeAddress(string address, TConfigurationItem config)
    {
        return address;
    }

    protected virtual string GetTokenContractAddress(TConfigurationItem config)
    {
        return config.SmartContractAddress;
    }

    private void LoadClientsFromConfiguration()
    {
        lock (this)
        {
            _walletRpcClients = GetConfigurations().ToImmutableDictionary(
                pair => pair.Key,
                pair => new RpcClient(pair.Value.JsonRpcUri, CreateHttpClient(pair.Key, pair.Value)));
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

    private static bool IsAvailable(USDtRpcSummary summary)
    {
        return summary is { Synced: true, RpcAvailable: true };
    }

    public async Task<(string, decimal?)[]> GetBalances(PaymentMethodId pmi, IEnumerable<string> addresses)
    {
        var configuration = GetConfigurations()[pmi];
        var tokenService = new StandardTokenService(GetWeb3Client(pmi), GetTokenContractAddress(configuration));
        var normalizedAddresses = addresses.Select(address => (address, normalized: NormalizeAddress(address, configuration)));

        List<(string, decimal?)> results = [];
        foreach (var (address, normalizedAddress) in normalizedAddresses)
            try
            {
                var balanceResult = await tokenService.BalanceOfQueryAsync(normalizedAddress);
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
        if (!_walletRpcClients!.TryGetValue(pmi, out _))
            return;

        var configuration = GetConfigurations()[pmi];
        var listenerState = await _settingsRepository.GetSettingAsync<EVMBasedListenerState>(GetListenerStateSettingKey(configuration));
        if (listenerState == null)
            return;

        var summary = new USDtRpcSummary();
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

            summary.Synced = summary.HighestBlockOnChain - listenerState.LastBlockHeight <
                             (int)(TimeSpan.FromMinutes(10).TotalSeconds / configuration.BlockTimeSeconds);

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
            _eventAggregator.Publish(new USDtDaemonStateChanged { Summary = summary, PaymentMethodId = pmi });
    }
}