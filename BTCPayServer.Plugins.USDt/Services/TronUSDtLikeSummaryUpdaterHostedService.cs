using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.USDt.Services;

public class TronUSDtLikeSummaryUpdaterHostedService(
    TronUSDtRPCProvider tronUSDtRpcProvider,
    TronUSDtLikeConfiguration tronUSDtLikeConfiguration,
    Logs logs)
    : IHostedService
{
    private CancellationTokenSource? _cts;


    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        foreach (var tronUSDtLikeConfigurationItem in tronUSDtLikeConfiguration.TronUSDtLikeConfigurationItems)
            _ = StartLoop(_cts.Token, tronUSDtLikeConfigurationItem.Key);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task StartLoop(CancellationToken cancellation, string cryptoCode)
    {
        logs.PayServer.LogInformation($"Starting listening TronUSDt-like daemons ({cryptoCode})");
        try
        {
            while (!cancellation.IsCancellationRequested)
                try
                {
                    await tronUSDtRpcProvider.UpdateSummary(cryptoCode);
                    if (tronUSDtRpcProvider.IsAvailable(cryptoCode))
                        await Task.Delay(TimeSpan.FromSeconds(60 * 5), cancellation);
                    else
                        await Task.Delay(TimeSpan.FromSeconds(30), cancellation);
                }
                catch (Exception ex) when (!cancellation.IsCancellationRequested)
                {
                    logs.PayServer.LogError(ex, $"Unhandled exception in Summary updater ({cryptoCode})");
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellation);
                }
        }
        catch when (cancellation.IsCancellationRequested)
        {
        }
    }
}