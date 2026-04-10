using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Services.Events;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.USDt.Services;

public class USDtPluginConfigurationBootstrapper(
    ISettingsRepository settingsRepository,
    USDtPluginConfiguration pluginConfiguration,
    EventAggregator eventAggregator) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var pair in pluginConfiguration.TronUSDtLikeConfigurationItems)
        {
            pluginConfiguration.TronUSDtLikeConfigurationItems[pair.Key] =
                await USDtPlugin.OverrideWithServerSettingsAsync(pair.Value, settingsRepository);
        }

        foreach (var pair in pluginConfiguration.EVMUSDtLikeConfigurationItems)
        {
            pluginConfiguration.EVMUSDtLikeConfigurationItems[pair.Key] =
                await USDtPlugin.OverrideWithServerSettingsAsync(pair.Value, settingsRepository);
        }

        eventAggregator.Publish(new USDtSettingsChanged());
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}