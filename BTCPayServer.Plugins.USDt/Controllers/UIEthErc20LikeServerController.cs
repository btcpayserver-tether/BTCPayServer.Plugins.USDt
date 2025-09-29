using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Filters;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.Ethereum;
using BTCPayServer.Plugins.USDt.Controllers.ViewModels;
using BTCPayServer.Plugins.USDt.Services.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NBXplorer;

namespace BTCPayServer.Plugins.USDt.Controllers;

[Route("server/ethErc20like")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIEthErc20LikeServerController(
    IConfiguration configuration,
    ISettingsRepository settingsRepository,
    NBXplorerNetworkProvider nbXplorerNetworkProvider,
    USDtPluginConfiguration usdtPluginConfiguration,
    EventAggregator eventAggregator) : Controller
{
    [HttpGet]
    public async Task<IActionResult> GetServerConfig()
    {
        var ethConfiguration = usdtPluginConfiguration.EthereumErc20LikeConfigurationItems.SingleOrDefault().Value;
        if (ethConfiguration is null)
            throw new InvalidOperationException();

        var defaultConfiguration = USDtPlugin.GetEthUSDtLikeDefaultConfigurationItem(nbXplorerNetworkProvider, configuration);
        var serverSettings = await settingsRepository.GetSettingAsync<EthUSDtLikeServerSettings>(USDtPlugin.ServerSettingsKey(ethConfiguration));

        var viewModel = new EthErc20LikeServerConfigViewModel
        {
            DefaultSmartContractAddress = defaultConfiguration.SmartContractAddress,
            DefaultJsonRpcUri = defaultConfiguration.JsonRpcUri,

            SmartContractAddress = serverSettings?.SmartContractAddress,
            JsonRpcUri = serverSettings?.JsonRpcUri?.AbsoluteUri
        };
        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetServerConfig(EthErc20LikeServerConfigViewModel viewModel)
    {
        var currentConfiguration = usdtPluginConfiguration.EthereumErc20LikeConfigurationItems.SingleOrDefault().Value;
        if (currentConfiguration is null)
            throw new InvalidOperationException();

        var defaultConfiguration = USDtPlugin.GetEthUSDtLikeDefaultConfigurationItem(nbXplorerNetworkProvider, configuration);
        if (!ModelState.IsValid)
        {
            viewModel.DefaultSmartContractAddress = defaultConfiguration.SmartContractAddress;
            viewModel.DefaultJsonRpcUri = defaultConfiguration.JsonRpcUri;
            return View(viewModel);
        }

        var serverSettings = new EthUSDtLikeServerSettings
        {
            SmartContractAddress = string.IsNullOrWhiteSpace(viewModel.SmartContractAddress) ? null : viewModel.SmartContractAddress,
            JsonRpcUri = string.IsNullOrWhiteSpace(viewModel.JsonRpcUri) ? null : new Uri(viewModel.JsonRpcUri)
        };

        await settingsRepository.UpdateSetting(serverSettings, USDtPlugin.ServerSettingsKey(currentConfiguration));

        usdtPluginConfiguration.EthereumErc20LikeConfigurationItems[currentConfiguration.GetPaymentMethodId()] =
            USDtPlugin.OverrideWithServerSettings(defaultConfiguration, settingsRepository);

        eventAggregator.Publish(new EthErc20SettingsChanged());

        return RedirectToAction("GetServerConfig");
    }
}
