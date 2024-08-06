using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Filters;
using BTCPayServer.Plugins.TronUSDT.Configuration;
using BTCPayServer.Plugins.TronUSDT.Controllers.ViewModels;
using BTCPayServer.Plugins.TronUSDT.Services;
using BTCPayServer.Plugins.TronUSDT.Services.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace BTCPayServer.Plugins.TronUSDT.Controllers;

[Route("server/tronUSDTlike")]
[OnlyIfSupport("TronUSDT")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UITronUSDTLikeServerController(
    IConfiguration configuration,
    ISettingsRepository settingsRepository,
    BTCPayNetworkProvider btcPayNetworkProvider,
    TronUSDTLikeConfiguration tronUSDTLikeConfiguration,
    EventAggregator eventAggregator) : Controller
{
    [HttpGet]
    public async Task<IActionResult> GetServerConfig()
    {
        var network = btcPayNetworkProvider.GetAll().OfType<TronUSDTLikeSpecificBtcPayNetwork>().SingleOrDefault();
        if (network is null) return NotFound();

        var serverSettings = await settingsRepository.GetSettingAsync<TronUSDTLikeServerSettings>(
            TronUSDTPlugin.ServerSettingsKey(network.CryptoCode));

        var viewModel = new TronUSDTLikeServerConfigViewModel()
        {
            DefaultSmartContractAddress = TronUSDTPlugin.GetDefaultSmartContractAddress(btcPayNetworkProvider.NetworkType, configuration, network),
            DefaultJsonRpcUri = TronUSDTPlugin.GetDefaultJsonRpcUri(btcPayNetworkProvider.NetworkType, configuration, network),

            SmartContractAddress = serverSettings?.SmartContractAddress,
            JsonRpcUri = serverSettings?.JsonRpcUri?.AbsoluteUri,
        };
        return View(viewModel);
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetServerConfig(TronUSDTLikeServerConfigViewModel viewModel)
    {
        var network = btcPayNetworkProvider.GetAll().OfType<TronUSDTLikeSpecificBtcPayNetwork>().SingleOrDefault();
        if (network is null) return NotFound();
        
        if (!ModelState.IsValid)
        {
            viewModel.DefaultSmartContractAddress = TronUSDTPlugin.GetDefaultSmartContractAddress(btcPayNetworkProvider.NetworkType, configuration, network);
            viewModel.DefaultJsonRpcUri = TronUSDTPlugin.GetDefaultJsonRpcUri(btcPayNetworkProvider.NetworkType, configuration, network);
            return View(viewModel);
        }

        TronUSDTLikeServerSettings serverSettings = new TronUSDTLikeServerSettings()
        {
            SmartContractAddress = viewModel.SmartContractAddress is null or "" ? null : viewModel.SmartContractAddress,
            JsonRpcUri = viewModel.JsonRpcUri is null or "" ? null : new Uri(viewModel.JsonRpcUri)
        };

        await settingsRepository.UpdateSetting(serverSettings, TronUSDTPlugin.ServerSettingsKey(network.CryptoCode));

        tronUSDTLikeConfiguration.TronUSDTLikeConfigurationItems[network.CryptoCode] =
            TronUSDTPlugin.GetConfigurationItem(btcPayNetworkProvider.NetworkType, serverSettings, configuration, network);


        eventAggregator.Publish(new TronUSDTSettingsChanged());

        return RedirectToAction("GetServerConfig");
    }
}