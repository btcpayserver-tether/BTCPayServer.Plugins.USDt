using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Filters;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Controllers.ViewModels;
using BTCPayServer.Plugins.USDt.Services;
using BTCPayServer.Plugins.USDt.Services.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace BTCPayServer.Plugins.USDt.Controllers;

[Route("server/tronUSDtlike")]
[OnlyIfSupport("TronUSDt")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UITronUSDtLikeServerController(
    IConfiguration configuration,
    ISettingsRepository settingsRepository,
    BTCPayNetworkProvider btcPayNetworkProvider,
    TronUSDtLikeConfiguration tronUSDtLikeConfiguration,
    EventAggregator eventAggregator) : Controller
{
    [HttpGet]
    public async Task<IActionResult> GetServerConfig()
    {
        var network = btcPayNetworkProvider.GetAll().OfType<TronUSDtLikeSpecificBtcPayNetwork>().SingleOrDefault();
        if (network is null) return NotFound();

        var serverSettings = await settingsRepository.GetSettingAsync<TronUSDtLikeServerSettings>(
            USDtPlugin.ServerSettingsKey(network.CryptoCode));

        var viewModel = new TronUSDtLikeServerConfigViewModel()
        {
            DefaultSmartContractAddress = USDtPlugin.GetDefaultSmartContractAddress(btcPayNetworkProvider.NetworkType, configuration, network),
            DefaultJsonRpcUri = USDtPlugin.GetDefaultJsonRpcUri(btcPayNetworkProvider.NetworkType, configuration, network),

            SmartContractAddress = serverSettings?.SmartContractAddress,
            JsonRpcUri = serverSettings?.JsonRpcUri?.AbsoluteUri,
        };
        return View(viewModel);
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetServerConfig(TronUSDtLikeServerConfigViewModel viewModel)
    {
        var network = btcPayNetworkProvider.GetAll().OfType<TronUSDtLikeSpecificBtcPayNetwork>().SingleOrDefault();
        if (network is null) return NotFound();

        if (!ModelState.IsValid)
        {
            viewModel.DefaultSmartContractAddress = USDtPlugin.GetDefaultSmartContractAddress(btcPayNetworkProvider.NetworkType, configuration, network);
            viewModel.DefaultJsonRpcUri = USDtPlugin.GetDefaultJsonRpcUri(btcPayNetworkProvider.NetworkType, configuration, network);
            return View(viewModel);
        }

        TronUSDtLikeServerSettings serverSettings = new TronUSDtLikeServerSettings()
        {
            SmartContractAddress = viewModel.SmartContractAddress is null or "" ? null : viewModel.SmartContractAddress,
            JsonRpcUri = viewModel.JsonRpcUri is null or "" ? null : new Uri(viewModel.JsonRpcUri)
        };

        await settingsRepository.UpdateSetting(serverSettings, USDtPlugin.ServerSettingsKey(network.CryptoCode));

        tronUSDtLikeConfiguration.TronUSDtLikeConfigurationItems[network.CryptoCode] =
            USDtPlugin.GetConfigurationItem(btcPayNetworkProvider.NetworkType, serverSettings, configuration, network);


        eventAggregator.Publish(new TronUSDtSettingsChanged());

        return RedirectToAction("GetServerConfig");
    }
}