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
    BTCPayNetworkProvider btcPayNetworkProvider) : Controller
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
        var tronUSDtLikeNetwork = btcPayNetworkProvider.GetAll().OfType<TronUSDtLikeSpecificBtcPayNetwork>();

        var vm = new ViewTronUSDtStoreOptionsViewModel();
        foreach (var network in tronUSDtLikeNetwork)
        {
            var paymentMethodId = TronUSDtLikePaymentType.Instance.GetPaymentMethodId(network.CryptoCode);
            var matchedPaymentMethod = storeData.GetPaymentMethodConfig<TronUSDtPaymentMethodConfig>(paymentMethodId, handlers);

            vm.Items.Add(new ViewTronUSDtStoreOptionItemViewModel
            {
                CryptoCode = network.CryptoCode,
                DisplayName = network.DisplayName,
                Enabled = matchedPaymentMethod != null && !excludeFilters.Match(paymentMethodId),
                Addresses = matchedPaymentMethod == null ? Array.Empty<string>() : matchedPaymentMethod.Addresses
            });
        }

        return vm;
    }


    [HttpGet("{cryptoCode}")]
    public async Task<IActionResult> GetStoreTronUSDtLikePaymentMethod(string cryptoCode)
    {
        var network = btcPayNetworkProvider.GetNetwork<TronUSDtLikeSpecificBtcPayNetwork>(cryptoCode);
        if (network is null) return NotFound();

        var excludeFilters = StoreData.GetStoreBlob().GetExcludedPaymentMethods();
        var paymentMethodId = TronUSDtLikePaymentType.Instance.GetPaymentMethodId(network.CryptoCode);
        var matchedPaymentMethodConfig =
            StoreData.GetPaymentMethodConfig<TronUSDtPaymentMethodConfig>(paymentMethodId, handlers);

        if (matchedPaymentMethodConfig == null)
            return View(new EditTronUSDtPaymentMethodViewModel
            {
                Enabled = false
            });

        var balances =
            await tronUSDtRpcProvider.GetBalances(cryptoCode, [.. matchedPaymentMethodConfig.Addresses]);
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

    [HttpPost("{cryptoCode}/addresses/{address}/delete")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> DeleteAddress(string storeId, string cryptoCode, string address)
    {
        var network = btcPayNetworkProvider.GetNetwork<TronUSDtLikeSpecificBtcPayNetwork>(cryptoCode);
        if (network is null) return NotFound();

        var store = StoreData;
        var blob = StoreData.GetStoreBlob();
        var paymentMethodId = TronUSDtLikePaymentType.Instance.GetPaymentMethodId(network.CryptoCode);

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

        return RedirectToAction(nameof(GetStoreTronUSDtLikePaymentMethod), new { storeId, cryptoCode });
    }

    [HttpPost("{cryptoCode}")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> GetStoreTronUSDtLikePaymentMethod(EditTronUSDtPaymentMethodViewModel viewModel,
        string cryptoCode)
    {
        var network = btcPayNetworkProvider.GetNetwork<TronUSDtLikeSpecificBtcPayNetwork>(cryptoCode);
        if (network is null) return NotFound();

        if (!ModelState.IsValid) return await GetStoreTronUSDtLikePaymentMethod(cryptoCode);

        var store = StoreData;
        var blob = StoreData.GetStoreBlob();
        var paymentMethodId = TronUSDtLikePaymentType.Instance.GetPaymentMethodId(network.CryptoCode);
        var currentPaymentMethodConfig =
            StoreData.GetPaymentMethodConfig<TronUSDtPaymentMethodConfig>(paymentMethodId, handlers);
        currentPaymentMethodConfig ??= new TronUSDtPaymentMethodConfig();

        if (string.IsNullOrEmpty(viewModel.Address) == false)
        {
            // if (TronUSDtAddressHelper.IsValid(viewModel.Address) == false)
            // {
            //     TempData.SetStatusMessageModel(new StatusMessageModel
            //     {
            //         Message = $"{viewModel.Address} is not a TRON address (Base58 format expected).",
            //         Severity = StatusMessageModel.StatusSeverity.Error
            //     });
            //
            //
            //     return RedirectToAction("GetStoreTronUSDtLikePaymentMethod", new { storeId = store.Id, cryptoCode });
            // }

            //todo check tron format
            if (currentPaymentMethodConfig.Addresses.Contains(viewModel.Address))
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = $"{viewModel.Address} is already configured to being tracked for {cryptoCode}.",
                    Severity = StatusMessageModel.StatusSeverity.Error
                });

                return RedirectToAction("GetStoreTronUSDtLikePaymentMethod", new { storeId = store.Id, cryptoCode = cryptoCode });
            }

            currentPaymentMethodConfig.Addresses =
            [
                .. currentPaymentMethodConfig.Addresses,
                .. new[] { viewModel.Address }
            ];

            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = $"{viewModel.Address} is now being tracked for {cryptoCode}",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
        }
        else if (viewModel.Enabled == blob.IsExcluded(paymentMethodId))
        {
            blob.SetExcluded(paymentMethodId, !viewModel.Enabled);

            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = $"{cryptoCode} is now {(viewModel.Enabled ? "enabled" : "disabled")}",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
        }

        StoreData.SetPaymentMethodConfig(handlers[paymentMethodId], currentPaymentMethodConfig);
        store.SetStoreBlob(blob);
        await storeRepository.UpdateStore(store);


        return RedirectToAction("GetStoreTronUSDtLikePaymentMethod", new { storeId = store.Id, cryptoCode });
    }
}