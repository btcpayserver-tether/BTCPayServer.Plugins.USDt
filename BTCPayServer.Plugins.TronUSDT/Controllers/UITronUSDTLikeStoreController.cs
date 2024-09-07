using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Plugins.TronUSDT.Controllers.ViewModels;
using BTCPayServer.Plugins.TronUSDT.Services;
using BTCPayServer.Plugins.TronUSDT.Services.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.TronUSDT.Controllers;

[Route("stores/{storeId}/tronUSDTlike")]
[OnlyIfSupport("TronUSDT")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UITronUSDTLikeStoreController(
    StoreRepository storeRepository,
    TronUSDTRPCProvider tronUSDTRpcProvider,
    PaymentMethodHandlerDictionary handlers,
    InvoiceRepository invoiceRepository,
    DisplayFormatter displayFormatter,
    BTCPayNetworkProvider btcPayNetworkProvider) : Controller
{
    private StoreData StoreData => HttpContext.GetStoreData();

    [HttpGet]
    public IActionResult GetStoreTronUSDTLikePaymentMethods()
    {
        var vm = GetVM(StoreData);

        return View(vm);
    }

    [NonAction]
    public ViewTronUSDTStoreOptionsViewModel GetVM(StoreData storeData)
    {
        var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();
        var tronUSDTLikeNetwork = btcPayNetworkProvider.GetAll().OfType<TronUSDTLikeSpecificBtcPayNetwork>();

        var vm = new ViewTronUSDTStoreOptionsViewModel();
        foreach (var network in tronUSDTLikeNetwork)
        {
            var paymentMethodId = TronUSDTPaymentType.Instance.GetPaymentMethodId(network.CryptoCode);
            var matchedPaymentMethod = storeData.GetPaymentMethodConfig<TronUSDTPaymentMethodConfig>(paymentMethodId, handlers);

            vm.Items.Add(new ViewTronUSDTStoreOptionItemViewModel
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
    public async Task<IActionResult> GetStoreTronUSDTLikePaymentMethod(string cryptoCode)
    {
        var network = btcPayNetworkProvider.GetNetwork<TronUSDTLikeSpecificBtcPayNetwork>(cryptoCode);
        if (network is null) return NotFound();

        var excludeFilters = StoreData.GetStoreBlob().GetExcludedPaymentMethods();
        var paymentMethodId = TronUSDTPaymentType.Instance.GetPaymentMethodId(network.CryptoCode);
        var matchedPaymentMethodConfig =
            StoreData.GetPaymentMethodConfig<TronUSDTPaymentMethodConfig>(paymentMethodId, handlers);

        if (matchedPaymentMethodConfig == null)
            return View(new EditTronUSDTPaymentMethodViewModel
            {
                Enabled = false
            });

        var balances =
            await tronUSDTRpcProvider.GetBalances(cryptoCode, [.. matchedPaymentMethodConfig.Addresses]);
        var reservedAddresses =
            await TronUSDTPaymentMethodConfig.GetReservedAddresses(paymentMethodId, invoiceRepository);

        return View(new EditTronUSDTPaymentMethodViewModel
        {
            Enabled = !excludeFilters.Match(paymentMethodId),
            Address = "",
            Addresses = matchedPaymentMethodConfig.Addresses.Select(s =>
                new EditTronUSDTPaymentMethodViewModel.EditTronUSDTPaymentMethodAddressViewModel
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
        var network = btcPayNetworkProvider.GetNetwork<TronUSDTLikeSpecificBtcPayNetwork>(cryptoCode);
        if (network is null) return NotFound();

        var store = StoreData;
        var blob = StoreData.GetStoreBlob();
        var paymentMethodId = TronUSDTPaymentType.Instance.GetPaymentMethodId(network.CryptoCode);

        var currentPaymentMethodConfig =
            StoreData.GetPaymentMethodConfig<TronUSDTPaymentMethodConfig>(paymentMethodId, handlers);
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

        return RedirectToAction(nameof(GetStoreTronUSDTLikePaymentMethod), new { storeId, cryptoCode });
    }

    [HttpPost("{cryptoCode}")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> GetStoreTronUSDTLikePaymentMethod(EditTronUSDTPaymentMethodViewModel viewModel,
        string cryptoCode)
    {
        var network = btcPayNetworkProvider.GetNetwork<TronUSDTLikeSpecificBtcPayNetwork>(cryptoCode);
        if (network is null) return NotFound();

        if (!ModelState.IsValid) return await GetStoreTronUSDTLikePaymentMethod(cryptoCode);

        var store = StoreData;
        var blob = StoreData.GetStoreBlob();
        var paymentMethodId = TronUSDTPaymentType.Instance.GetPaymentMethodId(network.CryptoCode);
        var currentPaymentMethodConfig =
            StoreData.GetPaymentMethodConfig<TronUSDTPaymentMethodConfig>(paymentMethodId, handlers);
        currentPaymentMethodConfig ??= new TronUSDTPaymentMethodConfig();

        if (string.IsNullOrEmpty(viewModel.Address) == false)
        {
            // if (TronUSDTAddressHelper.IsValid(viewModel.Address) == false)
            // {
            //     TempData.SetStatusMessageModel(new StatusMessageModel
            //     {
            //         Message = $"{viewModel.Address} is not a TRON address (Base58 format expected).",
            //         Severity = StatusMessageModel.StatusSeverity.Error
            //     });
            //
            //
            //     return RedirectToAction("GetStoreTronUSDTLikePaymentMethod", new { storeId = store.Id, cryptoCode });
            // }

            //todo check tron format
            if (currentPaymentMethodConfig.Addresses.Contains(viewModel.Address))
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = $"{viewModel.Address} is already configured to being tracked for {cryptoCode}.",
                    Severity = StatusMessageModel.StatusSeverity.Error
                });

                return RedirectToAction("GetStoreTronUSDTLikePaymentMethod", new { storeId = store.Id, cryptoCode });
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


        return RedirectToAction("GetStoreTronUSDTLikePaymentMethod", new { storeId = store.Id, cryptoCode });
    }
}