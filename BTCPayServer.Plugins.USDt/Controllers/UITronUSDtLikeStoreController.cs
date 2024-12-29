using System;
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
using BTCPayServer.Plugins.USDt.Services.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.USDt.Controllers;

[Route("stores/{storeId}/tronUSDtlike")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UITronUSDtLikeStoreController(
    StoreRepository storeRepository,
    TronUSDtRPCProvider tronUSDtRpcProvider,
    PaymentMethodHandlerDictionary handlers,
    InvoiceRepository invoiceRepository,
    DisplayFormatter displayFormatter,
    USDtPluginConfiguration pluginConfiguration) : Controller
{
    private StoreData StoreData => HttpContext.GetStoreData();

    [HttpGet]
    public IActionResult GetStoreTronUSDtLikePaymentMethods()
    {
        var vm = GetVM(StoreData);

        return View(vm);
    }

    [NonAction]
    public ViewTronUSDtStoreOptionsViewModel GetVM(StoreData storeData)
    {
        var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();

        var vm = new ViewTronUSDtStoreOptionsViewModel();
        foreach (var item in pluginConfiguration.TronUSDtLikeConfigurationItems.Values)
        {
            var pmi = item.GetPaymentMethodId();
            var matchedPaymentMethod = storeData.GetPaymentMethodConfig<TronUSDtPaymentMethodConfig>(pmi, handlers);
            vm.Items.Add(new ViewTronUSDtStoreOptionItemViewModel
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
    public async Task<IActionResult> GetStoreTronUSDtLikePaymentMethod(PaymentMethodId paymentMethodId)
    {
        if (pluginConfiguration.TronUSDtLikeConfigurationItems.ContainsKey(paymentMethodId) == false)
            return NotFound();
        
        var excludeFilters = StoreData.GetStoreBlob().GetExcludedPaymentMethods();
        var matchedPaymentMethodConfig =  StoreData.GetPaymentMethodConfig<TronUSDtPaymentMethodConfig>(paymentMethodId, handlers);

        if (matchedPaymentMethodConfig == null)
            return View(new EditTronUSDtPaymentMethodViewModel
            {
                Enabled = false
            });

        var balances =
            await tronUSDtRpcProvider.GetBalances(paymentMethodId, [.. matchedPaymentMethodConfig.Addresses]);
        var reservedAddresses =
            await TronUSDtPaymentMethodConfig.GetReservedAddresses(paymentMethodId, invoiceRepository);

        return View(new EditTronUSDtPaymentMethodViewModel
        {
            Enabled = !excludeFilters.Match(paymentMethodId),
            Address = "",
            Addresses = matchedPaymentMethodConfig.Addresses.Select(s =>
                new EditTronUSDtPaymentMethodViewModel.EditTronUSDtPaymentMethodAddressViewModel
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
        if (pluginConfiguration.TronUSDtLikeConfigurationItems.ContainsKey(paymentMethodId) == false)
            return NotFound();

        var store = StoreData;
        var blob = StoreData.GetStoreBlob();
        var currentPaymentMethodConfig =
            StoreData.GetPaymentMethodConfig<TronUSDtPaymentMethodConfig>(paymentMethodId, handlers);
        if (currentPaymentMethodConfig is null) return NotFound();

        currentPaymentMethodConfig.Addresses = currentPaymentMethodConfig.Addresses.Except(new[] { address }).ToArray();
        StoreData.SetPaymentMethodConfig(handlers[paymentMethodId], currentPaymentMethodConfig);
        store.SetStoreBlob(blob);
        await storeRepository.UpdateStore(store);

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = $"The address {address} was removed.",
            Severity = StatusMessageModel.StatusSeverity.Success
        });

        return RedirectToAction(nameof(GetStoreTronUSDtLikePaymentMethod), new { storeId, paymentMethodId });
    }

    [HttpPost("{paymentMethodId}")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> GetStoreTronUSDtLikePaymentMethod(EditTronUSDtPaymentMethodViewModel viewModel,
        PaymentMethodId paymentMethodId)
    {
        if (pluginConfiguration.TronUSDtLikeConfigurationItems.ContainsKey(paymentMethodId) == false)
            return NotFound();


        var store = StoreData;
        var blob = StoreData.GetStoreBlob();
        var currentPaymentMethodConfig = StoreData.GetPaymentMethodConfig<TronUSDtPaymentMethodConfig>(paymentMethodId, handlers);
        currentPaymentMethodConfig ??= new TronUSDtPaymentMethodConfig();

        if (string.IsNullOrEmpty(viewModel.Address) == false)
        {
            var addresses = viewModel.Address.Split([',', ';', ' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Where(TronUSDtAddressHelper.IsValid)
                .Where(s => currentPaymentMethodConfig.Addresses.Contains(s) == false).ToArray();
            
            if(addresses.Any() == false)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = "No addresses were added. Please make sure the addresses are valid and not already being tracked.",
                    Severity = StatusMessageModel.StatusSeverity.Error
                });

                return RedirectToAction("GetStoreTronUSDtLikePaymentMethod", new { storeId = store.Id, paymentMethodId = paymentMethodId });
            }
            
            currentPaymentMethodConfig.Addresses =
            [
                .. currentPaymentMethodConfig.Addresses,
                .. addresses
            ];


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
        else if (viewModel.Enabled == blob.IsExcluded(paymentMethodId))
        {
            blob.SetExcluded(paymentMethodId, !viewModel.Enabled);

            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = $"{paymentMethodId} is now {(viewModel.Enabled ? "enabled" : "disabled")}",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
        }

        StoreData.SetPaymentMethodConfig(handlers[paymentMethodId], currentPaymentMethodConfig);
        store.SetStoreBlob(blob);
        await storeRepository.UpdateStore(store);


        return RedirectToAction("GetStoreTronUSDtLikePaymentMethod", new { storeId = store.Id, paymentMethodId });
    }
}