using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Plugins.TronUSDT.Services;
using BTCPayServer.Plugins.TronUSDT.Services.Payments;
using BTCPayServer.Plugins.TronUSDT.Tron.TronUSDT;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Plugins.TronUSDT.Controllers;

public class ViewTronUSDTStoreOptionsViewModel
{
    public List<ViewTronUSDTStoreOptionItemViewModel> Items { get; } = [];
}

public class ViewTronUSDTStoreOptionItemViewModel
{
    public required string DisplayName { get; init; }
    public bool Enabled { get; init; }
    public required string CryptoCode { get; init; }
    public required string[] Addresses { get; set; }
}

public class EditTronUSDTPaymentMethodViewModel
{
    public string? Address { get; init; }
    public bool Enabled { get; init; }

    public EditTronUSDTPaymentMethodAddressViewModel[] Addresses { get; init; } =
        [];

    public class EditTronUSDTPaymentMethodAddressViewModel
    {
        public required string Value { get; init; }
        public bool Available { get; init; }
        public required string Balance { get; init; }
    }
}

[Route("stores/{storeId}/tronUSDTlike")]
[OnlyIfSupport("TronUSDT")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UITronUSDTLikeStoreController(
    StoreRepository storeRepository, TronUSDTRPCProvider tronUSDTRpcProvider,
    PaymentMethodHandlerDictionary handlers, InvoiceRepository invoiceRepository,
    DisplayFormatter displayFormatter,
    BTCPayNetworkProvider btcPayNetworkProvider) : Controller
{
    private StoreData StoreData => HttpContext.GetStoreData();

    [NonAction]
    public ViewTronUSDTStoreOptionsViewModel GetVM(StoreData storeData)
    {
        var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();
        var tronUSDTLikeNetwork = btcPayNetworkProvider.GetAll().OfType<TronUSDTLikeSpecificBtcPayNetwork>();

        var vm = new ViewTronUSDTStoreOptionsViewModel();
        foreach (var network in tronUSDTLikeNetwork)
        {
            var paymentMethodId = TronUSDTPaymentType.Instance.GetPaymentMethodId(network.CryptoCode);
            var matchedPaymentMethod =
                storeData.GetPaymentMethodConfig<TronUSDTPaymentMethodConfig>(paymentMethodId, handlers);

            if (matchedPaymentMethod == null)
                continue;
            
            vm.Items.Add(new ViewTronUSDTStoreOptionItemViewModel
            {
                CryptoCode = network.CryptoCode,
                DisplayName = network.DisplayName,
                Enabled = !excludeFilters.Match(paymentMethodId),
                Addresses = matchedPaymentMethod.Addresses,
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
                    Balance = displayFormatter.Currency(balances.Single(x => x.Item1 == s).Item2, "USDT"),
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
    public async Task<IActionResult> UpdatePaymentConfig(EditTronUSDTPaymentMethodViewModel viewModel,
        string cryptoCode)
    {
        var network = btcPayNetworkProvider.GetNetwork<TronUSDTLikeSpecificBtcPayNetwork>(cryptoCode);
        if (network is null) return NotFound();

        var store = StoreData;
        var blob = StoreData.GetStoreBlob();
        var paymentMethodId = TronUSDTPaymentType.Instance.GetPaymentMethodId(network.CryptoCode);
        var currentPaymentMethodConfig =
            StoreData.GetPaymentMethodConfig<TronUSDTPaymentMethodConfig>(paymentMethodId, handlers);
        currentPaymentMethodConfig ??= new TronUSDTPaymentMethodConfig();

        if (string.IsNullOrEmpty(viewModel.Address) == false)
        {
            if (TronUSDTAddressHelper.IsValid(viewModel.Address) == false)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = $"{viewModel.Address} is not a TRON address (Base58 format expected).",
                    Severity = StatusMessageModel.StatusSeverity.Error
                });


                return RedirectToAction("GetStoreTronUSDTLikePaymentMethod", new { storeId = store.Id, cryptoCode });
            }

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

            currentPaymentMethodConfig.Addresses = [.. currentPaymentMethodConfig.Addresses
, .. new[] { viewModel.Address }];

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