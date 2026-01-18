using System;
using System.Collections.Generic;
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
            HttpHeaders = HttpHeadersDictionaryToString(serverSettings?.HttpHeaders),
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
            JsonRpcUri = viewModel.JsonRpcUri is null or "" ? null : new Uri(viewModel.JsonRpcUri),
            HttpHeaders = ParseHttpHeadersString(viewModel.HttpHeaders)
        };

        await settingsRepository.UpdateSetting(serverSettings, USDtPlugin.ServerSettingsKey(currentConfiguration));

        usdtPluginConfiguration.TronUSDtLikeConfigurationItems[currentConfiguration.GetPaymentMethodId()] =
            USDtPlugin.OverrideWithServerSettings(tronUSDtDefaultConfiguration, settingsRepository);

        eventAggregator.Publish(new TronUSDtSettingsChanged());

        return RedirectToAction("GetServerConfig");
    }
    
    /// <summary>
    /// Parses a newline-separated string of "Header-Name: Value" pairs into a dictionary.
    /// </summary>
    private static Dictionary<string, string>? ParseHttpHeadersString(string? headersString)
    {
        if (string.IsNullOrWhiteSpace(headersString))
            return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = headersString.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0) continue;
            
            var headerName = line[..colonIndex].Trim();
            var headerValue = line[(colonIndex + 1)..].Trim();
            
            if (!string.IsNullOrEmpty(headerName) && !string.IsNullOrEmpty(headerValue))
            {
                headers[headerName] = headerValue;
            }
        }

        return headers.Count > 0 ? headers : null;
    }
    
    /// <summary>
    /// Converts a dictionary of headers to a newline-separated string of "Header-Name: Value" pairs.
    /// </summary>
    private static string? HttpHeadersDictionaryToString(Dictionary<string, string>? headers)
    {
        if (headers == null || headers.Count == 0)
            return null;

        return string.Join(Environment.NewLine, headers.Select(h => $"{h.Key}: {h.Value}"));
    }
}