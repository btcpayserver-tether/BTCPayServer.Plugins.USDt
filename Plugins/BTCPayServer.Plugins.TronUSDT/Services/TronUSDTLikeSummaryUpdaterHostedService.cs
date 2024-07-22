using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Plugins.TronUSDT.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.TronUSDT.Services;

public class TronUSDTLikeSummaryUpdaterHostedService(
    TronUSDTRPCProvider tronUSDTRpcProvider,
    TronUSDTLikeConfiguration tronUSDTLikeConfiguration,
    Logs logs)
    : IHostedService
{
    private CancellationTokenSource? _cts;


    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        foreach (var tronUSDTLikeConfigurationItem in tronUSDTLikeConfiguration.TronUSDTLikeConfigurationItems)
            _ = StartLoop(_cts.Token, tronUSDTLikeConfigurationItem.Key);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task StartLoop(CancellationToken cancellation, string cryptoCode)
    {
        logs.PayServer.LogInformation($"Starting listening TronUSDT-like daemons ({cryptoCode})");
        try
        {
            while (!cancellation.IsCancellationRequested)
                try
                {
                    await tronUSDTRpcProvider.UpdateSummary(cryptoCode);
                    if (tronUSDTRpcProvider.IsAvailable(cryptoCode))
                        await Task.Delay(TimeSpan.FromSeconds(30), cancellation);
                    else
                        await Task.Delay(TimeSpan.FromSeconds(10), cancellation);
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