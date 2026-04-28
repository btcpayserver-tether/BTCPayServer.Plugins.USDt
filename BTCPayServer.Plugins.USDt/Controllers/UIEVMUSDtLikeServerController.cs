using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Filters;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.EVM;
using BTCPayServer.Plugins.USDt.Controllers.ViewModels;
using BTCPayServer.Plugins.USDt.Services.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NBXplorer;

namespace BTCPayServer.Plugins.USDt.Controllers;

[Route("server/evmUSDtlike/{paymentMethodId}")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIEVMUSDtLikeServerController(
    IConfiguration configuration,
    ISettingsRepository settingsRepository,
    NBXplorerNetworkProvider nbXplorerNetworkProvider,
    USDtPluginConfiguration usdtPluginConfiguration,
    EventAggregator eventAggregator) : Controller
{
    [HttpGet]
    public async Task<IActionResult> GetServerConfig(PaymentMethodId paymentMethodId)
    {
        if (!usdtPluginConfiguration.EVMUSDtLikeConfigurationItems.TryGetValue(paymentMethodId, out var evmConfiguration))
            return NotFound();

        var defaultConfiguration = USDtConfigurationProvider.GetEVMUSDtDefaultConfigurationItem(paymentMethodId, nbXplorerNetworkProvider, configuration);
        var serverSettings = await settingsRepository.GetSettingAsync<EVMUSDtLikeServerSettings>(USDtConfigurationProvider.ServerSettingsKey(evmConfiguration));

        var viewModel = new EVMUSDtLikeServerConfigViewModel
        {
            DisplayName = evmConfiguration.DisplayName,
            ChainDisplayName = evmConfiguration.Chain,
            DefaultSmartContractAddress = defaultConfiguration.SmartContractAddress,
            DefaultJsonRpcUri = defaultConfiguration.JsonRpcUri,
            SmartContractAddress = serverSettings?.SmartContractAddress,
            JsonRpcUri = serverSettings?.JsonRpcUri?.AbsoluteUri
        };
        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetServerConfig(EVMUSDtLikeServerConfigViewModel viewModel, PaymentMethodId paymentMethodId)
    {
        if (!usdtPluginConfiguration.EVMUSDtLikeConfigurationItems.TryGetValue(paymentMethodId, out var currentConfiguration))
            return NotFound();

        var defaultConfiguration = USDtConfigurationProvider.GetEVMUSDtDefaultConfigurationItem(paymentMethodId, nbXplorerNetworkProvider, configuration);
        if (!ModelState.IsValid)
        {
            viewModel.DisplayName = currentConfiguration.DisplayName;
            viewModel.ChainDisplayName = currentConfiguration.Chain;
            viewModel.DefaultSmartContractAddress = defaultConfiguration.SmartContractAddress;
            viewModel.DefaultJsonRpcUri = defaultConfiguration.JsonRpcUri;
            return View(viewModel);
        }

        var serverSettings = new EVMUSDtLikeServerSettings
        {
            SmartContractAddress = string.IsNullOrWhiteSpace(viewModel.SmartContractAddress) ? null : viewModel.SmartContractAddress,
            JsonRpcUri = string.IsNullOrWhiteSpace(viewModel.JsonRpcUri) ? null : new Uri(viewModel.JsonRpcUri)
        };

        await settingsRepository.UpdateSetting(serverSettings, USDtConfigurationProvider.ServerSettingsKey(currentConfiguration));

        usdtPluginConfiguration.EVMUSDtLikeConfigurationItems[paymentMethodId] =
            await USDtConfigurationProvider.OverrideWithServerSettingsAsync(defaultConfiguration, settingsRepository);

        eventAggregator.Publish(new USDtSettingsChanged());

        return RedirectToAction(nameof(GetServerConfig), new { paymentMethodId });
    }
}
