using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.USDt.Services;

/// <summary>
/// Base class for EVM-based summary updater hosted services.
/// Periodically updates the sync summary for each payment method.
/// </summary>
/// <typeparam name="TConfig">The configuration item type</typeparam>
/// <typeparam name="TProvider">The RPC provider type</typeparam>
/// <typeparam name="TSettingsChanged">The settings changed event type</typeparam>
public abstract class EVMSummaryUpdaterHostedService<TConfig, TProvider, TSettingsChanged>(
    TProvider rpcProvider,
    Logs logs)
    : IHostedService
    where TConfig : USDtPluginConfigurationItem, IEVMConfigurationItem
    where TProvider : EVMRPCProvider<TConfig, TSettingsChanged>
{
    private CancellationTokenSource? _cts;

    protected abstract string ChainName { get; }
    protected abstract IDictionary<PaymentMethodId, TConfig> GetConfigurationItems();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        foreach (var configurationItem in GetConfigurationItems())
            _ = StartLoop(_cts.Token, configurationItem.Key);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task StartLoop(CancellationToken cancellation, PaymentMethodId pmi)
    {
        logs.PayServer.LogInformation($"Starting listening {ChainName} daemons ({pmi})");
        try
        {
            while (!cancellation.IsCancellationRequested)
                try
                {
                    await rpcProvider.UpdateSummary(pmi);
                    if (rpcProvider.IsAvailable(pmi))
                        await Task.Delay(TimeSpan.FromSeconds(60), cancellation);
                    else
                        await Task.Delay(TimeSpan.FromSeconds(30), cancellation);
                }
                catch (Exception ex) when (!cancellation.IsCancellationRequested)
                {
                    logs.PayServer.LogError(ex, $"Unhandled exception in Summary updater ({pmi})");
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellation);
                }
        }
        catch when (cancellation.IsCancellationRequested)
        {
        }
    }
}
