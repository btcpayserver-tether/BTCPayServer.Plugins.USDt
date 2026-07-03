using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Services.Events;
using BTCPayServer.Plugins.USDt.Services.Payments;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Plugins.USDt.Services;

public class USDtChainActivationService : IDisposable
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    private readonly StoreRepository _storeRepository;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly USDtPluginConfiguration _pluginConfiguration;
    private readonly IEventAggregatorSubscription[] _eventAggregatorSubscriptions;
    private readonly object _cacheLock = new();
    private HashSet<PaymentMethodId>? _activePaymentMethodIds;
    private DateTimeOffset _cacheExpiresAt;
    private long _cacheVersion;

    public USDtChainActivationService(
        StoreRepository storeRepository,
        PaymentMethodHandlerDictionary handlers,
        USDtPluginConfiguration pluginConfiguration,
        EventAggregator eventAggregator)
    {
        _storeRepository = storeRepository;
        _handlers = handlers;
        _pluginConfiguration = pluginConfiguration;
        _eventAggregatorSubscriptions =
        [
            eventAggregator.Subscribe<USDtSettingsChanged>(_ => Invalidate()),
            eventAggregator.SubscribeAny<StoreEvent>(_ => Invalidate())
        ];
    }

    public async Task<bool> IsActivatedAsync(PaymentMethodId paymentMethodId, CancellationToken cancellationToken)
    {
        var activePaymentMethodIds = await GetActivePaymentMethodIds(cancellationToken);
        return activePaymentMethodIds.Contains(paymentMethodId);
    }

    public void Invalidate()
    {
        lock (_cacheLock)
        {
            _cacheVersion++;
            _activePaymentMethodIds = null;
            _cacheExpiresAt = DateTimeOffset.MinValue;
        }
    }

    public void Dispose()
    {
        foreach (var subscription in _eventAggregatorSubscriptions)
            subscription.Dispose();
    }

    private async Task<HashSet<PaymentMethodId>> GetActivePaymentMethodIds(CancellationToken cancellationToken)
    {
        long cacheVersion;
        lock (_cacheLock)
        {
            if (_activePaymentMethodIds is not null && _cacheExpiresAt > DateTimeOffset.UtcNow)
                return [.. _activePaymentMethodIds];

            cacheVersion = _cacheVersion;
        }

        var activePaymentMethodIds = await LoadActivePaymentMethodIds(cancellationToken);
        lock (_cacheLock)
        {
            if (cacheVersion != _cacheVersion)
                return activePaymentMethodIds;

            _activePaymentMethodIds = activePaymentMethodIds;
            _cacheExpiresAt = DateTimeOffset.UtcNow.Add(CacheDuration);
            return [.. _activePaymentMethodIds];
        }
    }

    private async Task<HashSet<PaymentMethodId>> LoadActivePaymentMethodIds(CancellationToken cancellationToken)
    {
        var configuredPaymentMethodIds = GetConfiguredPaymentMethodIds().ToHashSet();
        var activePaymentMethodIds = new HashSet<PaymentMethodId>();
        if (configuredPaymentMethodIds.Count == 0)
            return activePaymentMethodIds;

        var stores = await _storeRepository.GetStores();
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var store in stores.Where(store => !store.Archived))
        {
            var excludedPaymentMethods = store.GetStoreBlob().GetExcludedPaymentMethods();
            var storeUpdated = false;
            foreach (var paymentMethodId in configuredPaymentMethodIds)
            {
                if (activePaymentMethodIds.Contains(paymentMethodId))
                    continue;

                var config = store.GetPaymentMethodConfig<USDtPaymentMethodConfig>(paymentMethodId, _handlers);
                if (!IsActivated(config, excludedPaymentMethods.Match(paymentMethodId)))
                    continue;

                activePaymentMethodIds.Add(paymentMethodId);
                if (config is { Activated: false })
                {
                    config.MarkActivated();
                    store.SetPaymentMethodConfig(_handlers[paymentMethodId], config);
                    storeUpdated = true;
                }
            }

            if (storeUpdated)
            {
                await _storeRepository.UpdateStore(store);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (activePaymentMethodIds.Count == configuredPaymentMethodIds.Count)
                break;
        }

        return activePaymentMethodIds;
    }

    private IEnumerable<PaymentMethodId> GetConfiguredPaymentMethodIds()
    {
        return _pluginConfiguration.TronUSDtLikeConfigurationItems.Keys
            .Concat(_pluginConfiguration.EVMUSDtLikeConfigurationItems.Keys);
    }

    internal static bool IsActivated(USDtPaymentMethodConfig? config, bool excluded)
    {
        return config?.ActivatesChain(excluded) is true;
    }
}
