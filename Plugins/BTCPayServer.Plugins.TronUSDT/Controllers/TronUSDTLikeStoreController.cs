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
using BTCPayServer.Plugins.TronUSDT.Configuration;
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
    public List<ViewTronUSDTStoreOptionItemViewModel> Items { get; set; } = new();
}

public class ViewTronUSDTStoreOptionItemViewModel
{
    public string DisplayName { get; set; }
    public bool IsToken { get; set; }
    public bool Enabled { get; set; }
    public string[] Addresses { get; set; }
    public string CryptoCode { get; set; }
}

public class EditTronUSDTPaymentMethodViewModel
{
    public string? Address { get; set; }
    public bool Enabled { get; set; }

    public EditTronUSDTPaymentMethodAddressViewModel[] Addresses { get; set; } =
        Array.Empty<EditTronUSDTPaymentMethodAddressViewModel>();

    public class EditTronUSDTPaymentMethodAddressViewModel
    {
        public string Value { get; set; }
        public bool Available { get; set; }
        public string Balance { get; set; }
    }
}

[Route("stores/{storeId}/tronUSDTlike")]
[OnlyIfSupport("TronUSDT")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UITronUSDTLikeStoreController : Controller
{
    private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    private readonly DisplayFormatter _displayFormatter;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly InvoiceRepository _invoiceRepository;
    private readonly StoreRepository _StoreRepository;
    private readonly TronUSDTLikeConfiguration _TronUSDTLikeConfiguration;
    private readonly TronUSDTRPCProvider _TronUSDTRpcProvider;

    public UITronUSDTLikeStoreController(TronUSDTLikeConfiguration tronUSDTLikeConfiguration,
        StoreRepository storeRepository, TronUSDTRPCProvider tronUSDTRpcProvider,
        PaymentMethodHandlerDictionary handlers, InvoiceRepository invoiceRepository,
        DisplayFormatter displayFormatter,
        BTCPayNetworkProvider btcPayNetworkProvider)
    {
        _TronUSDTLikeConfiguration = tronUSDTLikeConfiguration;
        _StoreRepository = storeRepository;
        _TronUSDTRpcProvider = tronUSDTRpcProvider;
        _handlers = handlers;
        _invoiceRepository = invoiceRepository;
        _displayFormatter = displayFormatter;
        _btcPayNetworkProvider = btcPayNetworkProvider;
    }

    public StoreData StoreData => HttpContext.GetStoreData();

    [HttpGet]
    public async Task<IActionResult> GetStoreTronUSDTLikePaymentMethods()
    {
        var vm = await GetVM(StoreData);

        return View(vm);
    }

    [NonAction]
    public async Task<ViewTronUSDTStoreOptionsViewModel> GetVM(StoreData storeData)
    {
        var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();
        var tronUSDTLikeNetwork = _btcPayNetworkProvider.GetAll().OfType<TronUSDTLikeSpecificBtcPayNetwork>();

        var vm = new ViewTronUSDTStoreOptionsViewModel();
        foreach (var network in tronUSDTLikeNetwork)
        {
            var paymentMethodId = TronUSDTPaymentType.Instance.GetPaymentMethodId(network.CryptoCode);
            var matchedPaymentMethod =
                storeData.GetPaymentMethodConfig<TronUSDTPaymentMethodConfig>(paymentMethodId, _handlers);

            vm.Items.Add(new ViewTronUSDTStoreOptionItemViewModel
            {
                CryptoCode = network.CryptoCode,
                Addresses = matchedPaymentMethod?.Addresses,
                DisplayName = network.DisplayName,
                Enabled = matchedPaymentMethod != null && !excludeFilters.Match(paymentMethodId),
                IsToken = true
            });
        }

        return vm;
    }

    // [NonAction]
    //         public async Task<TronUSDTLikePaymentMethodListViewModel> GetVM(StoreData storeData)
    //         {
    //             var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();
    //
    //             var accountsList = _TronUSDTLikeConfiguration.TronUSDTLikeConfigurationItems.ToDictionary(pair => pair.Key,
    //                 pair => GetAccounts(pair.Key));
    //
    //             await Task.WhenAll(accountsList.Values);
    //             return new TronUSDTLikePaymentMethodListViewModel()
    //             {
    //                 Items = _TronUSDTLikeConfiguration.TronUSDTLikeConfigurationItems.Select(pair =>
    //                     GetTronUSDTLikePaymentMethodViewModel(storeData, pair.Key, excludeFilters,
    //                         accountsList[pair.Key].Result))
    //             };
    //         }

    // private Task<GetAccountsResponse> GetAccounts(string cryptoCode)
    // {
    //     try
    //     {
    //         if (_TronUSDTRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary) && summary.RpcAvailable)
    //         {
    //
    //             return _TronUSDTRpcProvider.WalletRpcClients[cryptoCode].SendCommandAsync<GetAccountsRequest, GetAccountsResponse>("get_accounts", new GetAccountsRequest());
    //         }
    //     }
    //     catch { }
    //     return Task.FromResult<GetAccountsResponse>(null);
    // }

    // private TronUSDTLikePaymentMethodViewModel GetTronUSDTLikePaymentMethodViewModel(
    //     StoreData storeData, string cryptoCode,
    //     IPaymentFilter excludeFilters, GetAccountsResponse accountsResponse)
    // {
    //     var tronUSDT = storeData.GetPaymentMethodConfigs(_handlers)
    //         .Where(s => s.Value is TronUSDTPaymentPromptDetails)
    //         .Select(s => (PaymentMethodId: s.Key, Details: (TronUSDTPaymentPromptDetails)s.Value));
    //     var pmi = TronUSDTPaymentType.Instance.GetPaymentMethodId(cryptoCode);
    //     var settings = tronUSDT.Where(method => method.PaymentMethodId == pmi).Select(m => m.Details).SingleOrDefault();
    //     _TronUSDTRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary);
    //     _TronUSDTLikeConfiguration.TronUSDTLikeConfigurationItems.TryGetValue(cryptoCode,
    //         out var configurationItem);
    //     var accounts = accountsResponse?.SubaddressAccounts?.Select(account =>
    //         new SelectListItem(
    //             $"{account.AccountIndex} - {(string.IsNullOrEmpty(account.Label) ? "No label" : account.Label)}",
    //             account.AccountIndex.ToString(CultureInfo.InvariantCulture)));
    //
    //     var settlementThresholdChoice = TronUSDTLikeSettlementThresholdChoice.StoreSpeedPolicy;
    //     if (settings != null && settings.InvoiceSettledConfirmationThreshold is { } confirmations)
    //     {
    //         settlementThresholdChoice = confirmations switch
    //         {
    //             0 => TronUSDTLikeSettlementThresholdChoice.ZeroConfirmation,
    //             1 => TronUSDTLikeSettlementThresholdChoice.AtLeastOne,
    //             10 => TronUSDTLikeSettlementThresholdChoice.AtLeastTen,
    //             _ => TronUSDTLikeSettlementThresholdChoice.Custom
    //         };
    //     }
    //
    //     return new TronUSDTLikePaymentMethodViewModel()
    //     {
    //         Enabled =
    //             settings != null &&
    //             !excludeFilters.Match(TronUSDTPaymentType.Instance.GetPaymentMethodId(cryptoCode)),
    //         Summary = summary,
    //         CryptoCode = cryptoCode,
    //         AccountIndex = settings?.AccountIndex ?? accountsResponse?.SubaddressAccounts?.FirstOrDefault()?.AccountIndex ?? 0,
    //         Accounts = accounts == null ? null : new SelectList(accounts, nameof(SelectListItem.Value),
    //             nameof(SelectListItem.Text)),
    //         SettlementConfirmationThresholdChoice = settlementThresholdChoice,
    //         CustomSettlementConfirmationThreshold =
    //             settings != null &&
    //             settlementThresholdChoice is TronUSDTLikeSettlementThresholdChoice.Custom
    //                 ? settings.InvoiceSettledConfirmationThreshold
    //                 : null
    //     };
    // }

    [HttpGet("{cryptoCode}")]
    public async Task<IActionResult> GetStoreTronUSDTLikePaymentMethod(string cryptoCode)
    {
        var network = _btcPayNetworkProvider.GetNetwork<TronUSDTLikeSpecificBtcPayNetwork>(cryptoCode);
        if (network is null) return NotFound();

        var excludeFilters = StoreData.GetStoreBlob().GetExcludedPaymentMethods();
        var paymentMethodId = TronUSDTPaymentType.Instance.GetPaymentMethodId(network.CryptoCode);
        var matchedPaymentMethodConfig =
            StoreData.GetPaymentMethodConfig<TronUSDTPaymentMethodConfig>(paymentMethodId, _handlers);


        if (matchedPaymentMethodConfig == null)
            return View(new EditTronUSDTPaymentMethodViewModel
            {
                Enabled = false
            });

        var balances =
            await _TronUSDTRpcProvider.GetBalances(cryptoCode, matchedPaymentMethodConfig.Addresses.ToArray());
        var reservedAddresses =
            await matchedPaymentMethodConfig.GetReservedAddresses(paymentMethodId, _invoiceRepository);
        return View(new EditTronUSDTPaymentMethodViewModel
        {
            Enabled = !excludeFilters.Match(paymentMethodId),
            Address = "",
            Addresses = matchedPaymentMethodConfig.Addresses.Select(s =>
                new EditTronUSDTPaymentMethodViewModel.EditTronUSDTPaymentMethodAddressViewModel
                {
                    Available = reservedAddresses.Contains(s) == false,
                    Balance = _displayFormatter.Currency(balances.Single(x => x.Item1 == s).Item2, "USDT"),
                    Value = s
                }).ToArray()
        });

        // cryptoCode = cryptoCode.ToUpperInvariant();
        // if (!_TronUSDTLikeConfiguration.TronUSDTLikeConfigurationItems.ContainsKey(cryptoCode))
        // {
        //     return NotFound();
        // }
        //
        // var vm = GetTronUSDTLikePaymentMethodViewModel(StoreData, cryptoCode,
        //     StoreData.GetStoreBlob().GetExcludedPaymentMethods(), await GetAccounts(cryptoCode));
        // return View(nameof(GetStoreTronUSDTLikePaymentMethod), vm);
    }

    [HttpPost("{cryptoCode}/addresses/{address}/delete")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> DeleteAddress(string storeId, string cryptoCode, string address)
    {
        var network = _btcPayNetworkProvider.GetNetwork<TronUSDTLikeSpecificBtcPayNetwork>(cryptoCode);
        if (network is null) return NotFound();

        var store = StoreData;
        var blob = StoreData.GetStoreBlob();
        var paymentMethodId = TronUSDTPaymentType.Instance.GetPaymentMethodId(network.CryptoCode);

        var currentPaymentMethodConfig =
            StoreData.GetPaymentMethodConfig<TronUSDTPaymentMethodConfig>(paymentMethodId, _handlers);
        if (currentPaymentMethodConfig is null) return NotFound();

        currentPaymentMethodConfig.Addresses = currentPaymentMethodConfig.Addresses.Except(new[] { address }).ToArray();
        StoreData.SetPaymentMethodConfig(_handlers[paymentMethodId], currentPaymentMethodConfig);
        store.SetStoreBlob(blob);
        await _StoreRepository.UpdateStore(store);

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
        var network = _btcPayNetworkProvider.GetNetwork<TronUSDTLikeSpecificBtcPayNetwork>(cryptoCode);
        if (network is null) return NotFound();

        var store = StoreData;
        var blob = StoreData.GetStoreBlob();
        var paymentMethodId = TronUSDTPaymentType.Instance.GetPaymentMethodId(network.CryptoCode);
        var currentPaymentMethodConfig =
            StoreData.GetPaymentMethodConfig<TronUSDTPaymentMethodConfig>(paymentMethodId, _handlers);
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

            currentPaymentMethodConfig.Addresses = currentPaymentMethodConfig.Addresses
                .Concat(new[] { viewModel.Address }).ToArray();

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

        StoreData.SetPaymentMethodConfig(_handlers[paymentMethodId], currentPaymentMethodConfig);
        store.SetStoreBlob(blob);
        await _StoreRepository.UpdateStore(store);


        return RedirectToAction("GetStoreTronUSDTLikePaymentMethod", new { storeId = store.Id, cryptoCode });
    }

    public class TronUSDTLikePaymentMethodListViewModel
    {
        public IEnumerable<TronUSDTLikePaymentMethodViewModel> Items { get; set; }
    }

    public class TronUSDTLikePaymentMethodViewModel : IValidatableObject
    {
        public TronUSDTRPCProvider.TronUSDTLikeSummary Summary { get; set; }
        public string CryptoCode { get; set; }
        public string NewAccountLabel { get; set; }
        public long AccountIndex { get; set; }
        public bool Enabled { get; set; }

        public IEnumerable<SelectListItem> Accounts { get; set; }
        public bool WalletFileFound { get; set; }

        [Display(Name = "View-Only Wallet File")]
        public IFormFile WalletFile { get; set; }

        [Display(Name = "Wallet Keys File")] public IFormFile WalletKeysFile { get; set; }

        [Display(Name = "Wallet Password")] public string WalletPassword { get; set; }
        // [Display(Name = "Consider the invoice settled when the payment transaction �")]
        // public TronUSDTLikeSettlementThresholdChoice SettlementConfirmationThresholdChoice { get; set; }
        // [Display(Name = "Required Confirmations"), Range(0, 100)]
        // public long? CustomSettlementConfirmationThreshold { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // if (SettlementConfirmationThresholdChoice is TronUSDTLikeSettlementThresholdChoice.Custom
            //     && CustomSettlementConfirmationThreshold is null)
            // {
            //     yield return new ValidationResult(
            //         "You must specify the number of required confirmations when using a custom threshold.",
            //         new[] { nameof(CustomSettlementConfirmationThreshold) });
            // }

            yield break;
        }
    }

    // public enum TronUSDTLikeSettlementThresholdChoice
    // {
    //     [Display(Name = "Store Speed Policy", Description = "Use the store's speed policy")]
    //     StoreSpeedPolicy,
    //     [Display(Name = "Zero Confirmation", Description = "Is unconfirmed")]
    //     ZeroConfirmation,
    //     [Display(Name = "At Least One", Description = "Has at least 1 confirmation")]
    //     AtLeastOne,
    //     [Display(Name = "At Least Ten", Description = "Has at least 10 confirmations")]
    //     AtLeastTen,
    //     [Display(Name = "Custom", Description = "Custom")]
    //     Custom
    // }
}