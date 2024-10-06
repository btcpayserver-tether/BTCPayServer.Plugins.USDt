using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Filters;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.Tron;
using BTCPayServer.Plugins.USDt.Controllers.ViewModels;
using BTCPayServer.Plugins.USDt.Services;
using BTCPayServer.Plugins.USDt.Services.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NBXplorer;

namespace BTCPayServer.Plugins.USDt.Controllers;

[Route("server/tronUSDtlike")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UITronUSDtLikeServerController(
    IConfiguration configuration,
    ISettingsRepository settingsRepository,
    NBXplorerNetworkProvider nbXplorerNetworkProvider,
    USDtPluginConfiguration usdtPluginConfiguration,
    EventAggregator eventAggregator) : Controller
{
    [HttpGet]
    public async Task<IActionResult> GetServerConfig()
    {
        var tronUSDtConfiguration = usdtPluginConfiguration.TronUSDtLikeConfigurationItems.SingleOrDefault().Value;
        if (tronUSDtConfiguration is null)
            throw new InvalidOperationException();

        var tronUSDtDefaultConfiguration = USDtPlugin.GetTronUSDtLikeDefaultConfigurationItem(nbXplorerNetworkProvider, configuration);

        var serverSettings = await settingsRepository.GetSettingAsync<TronUSDtLikeServerSettings>(USDtPlugin.ServerSettingsKey(tronUSDtConfiguration));

        var viewModel = new TronUSDtLikeServerConfigViewModel()
        {
            DefaultSmartContractAddress = tronUSDtDefaultConfiguration.SmartContractAddress,
            DefaultJsonRpcUri = tronUSDtDefaultConfiguration.JsonRpcUri,

            SmartContractAddress = serverSettings?.SmartContractAddress,
            JsonRpcUri = serverSettings?.JsonRpcUri?.AbsoluteUri,
        };
        return View(viewModel);
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetServerConfig(TronUSDtLikeServerConfigViewModel viewModel)
    {
        var currentConfiguration = usdtPluginConfiguration.TronUSDtLikeConfigurationItems.SingleOrDefault().Value;
        if (currentConfiguration is null)
            throw new InvalidOperationException();
        
        var tronUSDtDefaultConfiguration = USDtPlugin.GetTronUSDtLikeDefaultConfigurationItem(nbXplorerNetworkProvider, configuration);
        if (!ModelState.IsValid)
        {
            viewModel.DefaultSmartContractAddress = tronUSDtDefaultConfiguration.SmartContractAddress;
            viewModel.DefaultJsonRpcUri = tronUSDtDefaultConfiguration.JsonRpcUri;
            return View(viewModel);
        }

        var serverSettings = new TronUSDtLikeServerSettings()
        {
            SmartContractAddress = viewModel.SmartContractAddress is null or "" ? null : viewModel.SmartContractAddress,
            JsonRpcUri = viewModel.JsonRpcUri is null or "" ? null : new Uri(viewModel.JsonRpcUri)
        };

        await settingsRepository.UpdateSetting(serverSettings, USDtPlugin.ServerSettingsKey(currentConfiguration));

        usdtPluginConfiguration.TronUSDtLikeConfigurationItems[currentConfiguration.GetPaymentMethodId()] =
            USDtPlugin.OverrideWithServerSettings(tronUSDtDefaultConfiguration, settingsRepository);

        eventAggregator.Publish(new TronUSDtSettingsChanged());

        return RedirectToAction("GetServerConfig");
    }
}