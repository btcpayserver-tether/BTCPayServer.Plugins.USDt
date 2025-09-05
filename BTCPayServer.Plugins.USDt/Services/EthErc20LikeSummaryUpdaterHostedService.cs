using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.USDt.Services;

public class EthErc20LikeSummaryUpdaterHostedService(
    EthErc20RPCProvider rpcProvider,
    USDtPluginConfiguration usdtPluginConfiguration,
    Logs logs)
    : IHostedService
{
    private CancellationTokenSource? _cts;


    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        foreach (var configurationItem in usdtPluginConfiguration.EthereumErc20LikeConfigurationItems)
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
        logs.PayServer.LogInformation($"Starting listening Ethereum ERC20 daemons ({pmi})");
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
