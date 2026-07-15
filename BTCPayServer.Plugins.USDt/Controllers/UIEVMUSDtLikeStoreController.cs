using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Controllers.ViewModels;
using BTCPayServer.Plugins.USDt.Services;
using BTCPayServer.Plugins.USDt.Services.Events;
using BTCPayServer.Plugins.USDt.Services.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.USDt.Controllers;

[Route("stores/{storeId}/evmUSDtlike")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIEVMUSDtLikeStoreController(
    StoreRepository storeRepository,
    EVMUSDtRPCProvider evmUsdTRpcProvider,
    PaymentMethodHandlerDictionary handlers,
    InvoiceRepository invoiceRepository,
    DisplayFormatter displayFormatter,
    USDtPluginConfiguration pluginConfiguration,
    EventAggregator eventAggregator) : Controller
{
    private StoreData StoreData => HttpContext.GetStoreData();

    [HttpGet]
    public IActionResult GetStoreEVMUSDtLikePaymentMethods()
    {
        var vm = GetVM(StoreData);
        return View(vm);
    }

    [NonAction]
    public ViewUSDtStoreOptionsViewModel GetVM(StoreData storeData)
    {
        var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();

        var vm = new ViewUSDtStoreOptionsViewModel();
        foreach (var item in pluginConfiguration.EVMUSDtLikeConfigurationItems.Values)
        {
            var pmi = item.GetPaymentMethodId();
            var matchedPaymentMethod = storeData.GetPaymentMethodConfig<EVMUSDtPaymentMethodConfig>(pmi, handlers);
            vm.Items.Add(new ViewUSDtStoreOptionItemViewModel
            {
                PaymentMethodId = pmi,
                DisplayName = item.DisplayName,
                Enabled = matchedPaymentMethod != null && !excludeFilters.Match(pmi),
                Addresses = matchedPaymentMethod == null ? Array.Empty<string>() : matchedPaymentMethod.Addresses
            });
        }

        return vm;
    }

    [HttpGet("{paymentMethodId}")]
    public async Task<IActionResult> GetStoreEVMUSDtLikePaymentMethod(PaymentMethodId paymentMethodId)
    {
        if (!pluginConfiguration.EVMUSDtLikeConfigurationItems.TryGetValue(paymentMethodId, out var config))
            return NotFound();

        var excludeFilters = StoreData.GetStoreBlob().GetExcludedPaymentMethods();
        var matchedPaymentMethodConfig = StoreData.GetPaymentMethodConfig<EVMUSDtPaymentMethodConfig>(paymentMethodId, handlers);

        if (matchedPaymentMethodConfig == null)
            return View(new EditEVMUSDtPaymentMethodViewModel
            {
                DisplayName = config.DisplayName,
                ChainDisplayName = config.Chain,
                Enabled = false
            });

        var balances =
            await evmUsdTRpcProvider.GetBalances(paymentMethodId, [.. matchedPaymentMethodConfig.Addresses]);
        var reservedAddresses =
            await EVMUSDtPaymentMethodConfig.GetReservedAddresses(paymentMethodId, invoiceRepository);

        return View(new EditEVMUSDtPaymentMethodViewModel
        {
            DisplayName = config.DisplayName,
            ChainDisplayName = config.Chain,
            Enabled = !excludeFilters.Match(paymentMethodId),
            Address = "",
            PaymentLinkTemplate = matchedPaymentMethodConfig.PaymentLinkTemplate,
            Addresses = matchedPaymentMethodConfig.Addresses.Select(s =>
                new EditEVMUSDtPaymentMethodViewModel.EditEVMUSDtPaymentMethodAddressViewModel
                {
                    Available = reservedAddresses.Contains(s) == false,
                    Balance = balances.Single(x => x.Item1 == s).Item2 == null
                        ? "N/A"
                        : displayFormatter.Currency(balances.Single(x => x.Item1 == s).Item2!.Value, "USD\u20ae"),
                    Value = s
                }).ToArray()
        });
    }

    [HttpPost("{paymentMethodId}/addresses/{address}/delete")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> DeleteAddress(string storeId, PaymentMethodId paymentMethodId, string address)
    {
        if (pluginConfiguration.EVMUSDtLikeConfigurationItems.ContainsKey(paymentMethodId) == false)
            return NotFound();

        var store = StoreData;
        var blob = StoreData.GetStoreBlob();
        var currentPaymentMethodConfig =
            StoreData.GetPaymentMethodConfig<EVMUSDtPaymentMethodConfig>(paymentMethodId, handlers);
        if (currentPaymentMethodConfig is null) return NotFound();

        currentPaymentMethodConfig.MarkActivated();
        currentPaymentMethodConfig.Addresses = currentPaymentMethodConfig.Addresses.Except(new[] { address }).ToArray();
        StoreData.SetPaymentMethodConfig(handlers[paymentMethodId], currentPaymentMethodConfig);
        store.SetStoreBlob(blob);
        await storeRepository.UpdateStore(store);
        eventAggregator.Publish(new USDtSettingsChanged());

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = $"The address {address} was removed.",
            Severity = StatusMessageModel.StatusSeverity.Success
        });

        return RedirectToAction(nameof(GetStoreEVMUSDtLikePaymentMethod), new { storeId, paymentMethodId });
    }

    [HttpPost("{paymentMethodId}")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> GetStoreEVMUSDtLikePaymentMethod(EditEVMUSDtPaymentMethodViewModel viewModel,
        PaymentMethodId paymentMethodId)
    {
        if (pluginConfiguration.EVMUSDtLikeConfigurationItems.ContainsKey(paymentMethodId) == false)
            return NotFound();

        var store = StoreData;
        var blob = StoreData.GetStoreBlob();
        var currentPaymentMethodConfig = StoreData.GetPaymentMethodConfig<EVMUSDtPaymentMethodConfig>(paymentMethodId, handlers);
        currentPaymentMethodConfig ??= new EVMUSDtPaymentMethodConfig();

        if (string.IsNullOrEmpty(viewModel.Address) == false)
        {
            var addresses = viewModel.Address.Split(new char[] { ',', ';', ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(EVMAddressHelper.IsValid)
                .Select(a => a.ToLowerInvariant())
                .Where(s => currentPaymentMethodConfig.Addresses.Contains(s) == false).ToArray();

            if (addresses.Any() == false)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = "No addresses were added. Please make sure the addresses are valid and not already being tracked.",
                    Severity = StatusMessageModel.StatusSeverity.Error
                });

                return RedirectToAction(nameof(GetStoreEVMUSDtLikePaymentMethod), new { storeId = store.Id, paymentMethodId });
            }

            currentPaymentMethodConfig.Addresses =
            [
                .. currentPaymentMethodConfig.Addresses,
                .. addresses
            ];
            currentPaymentMethodConfig.MarkActivated();

            if (addresses.Length == 1)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = $"{addresses[0]} is now being tracked for {paymentMethodId}",
                    Severity = StatusMessageModel.StatusSeverity.Success
                });
            }
            else
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = $"{addresses.Length} addresses were added to {paymentMethodId}",
                    Severity = StatusMessageModel.StatusSeverity.Success
                });
            }
        }
        else
        {
            var messages = new List<string>();
            if (viewModel.Enabled)
                currentPaymentMethodConfig.MarkActivated();

            if (viewModel.Enabled == blob.IsExcluded(paymentMethodId))
            {
                blob.SetExcluded(paymentMethodId, !viewModel.Enabled);

                messages.Add($"{paymentMethodId} is now {(viewModel.Enabled ? "enabled" : "disabled")}");
            }

            if (currentPaymentMethodConfig.PaymentLinkTemplate != viewModel.PaymentLinkTemplate)
            {
                currentPaymentMethodConfig.PaymentLinkTemplate = viewModel.PaymentLinkTemplate;
                messages.Add("Payment link template updated");
            }
        }

        StoreData.SetPaymentMethodConfig(handlers[paymentMethodId], currentPaymentMethodConfig);
        store.SetStoreBlob(blob);
        await storeRepository.UpdateStore(store);
        eventAggregator.Publish(new USDtSettingsChanged());

        return RedirectToAction(nameof(GetStoreEVMUSDtLikePaymentMethod), new { storeId = store.Id, paymentMethodId });
    }
}
