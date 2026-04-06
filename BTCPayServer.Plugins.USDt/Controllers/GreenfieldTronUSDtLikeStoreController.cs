using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Controllers.Models;
using BTCPayServer.Plugins.USDt.Services;
using BTCPayServer.Plugins.USDt.Services.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.USDt.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
[EnableCors(CorsPolicies.All)]
public class GreenfieldTronUSDtLikeStoreController(
    TronUSDtRPCProvider tronUSDtRpcProvider,
    PaymentMethodHandlerDictionary handlers,
    InvoiceRepository invoiceRepository,
    USDtPluginConfiguration pluginConfiguration) : ControllerBase
{
    private StoreData StoreData => HttpContext.GetStoreDataOrNull()!;

    [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [HttpGet("~/api/v1/stores/{storeId}/tronUSDtlike/{paymentMethodId}")]
    public async Task<IActionResult> GetUSDtLikeStoreInformation(PaymentMethodId paymentMethodId)
    {
        if (pluginConfiguration.TronUSDtLikeConfigurationItems.ContainsKey(paymentMethodId) == false)
            return NotFound();

        var excludeFilters = StoreData.GetStoreBlob().GetExcludedPaymentMethods();
        var matchedPaymentMethodConfig =
            StoreData.GetPaymentMethodConfig<TronUSDtPaymentMethodConfig>(paymentMethodId, handlers);

        if (matchedPaymentMethodConfig == null)
            return NotFound();

        var balances =
            await tronUSDtRpcProvider.GetBalances(paymentMethodId, [.. matchedPaymentMethodConfig.Addresses]);
        var reservedAddresses =
            await TronUSDtPaymentMethodConfig.GetReservedAddresses(paymentMethodId, invoiceRepository);

        var data = new TronUSDtPaymentMethodInformation
        {
            StoreId = StoreData.Id,
            PaymentMethodId = paymentMethodId.ToString(),
            Enabled = !excludeFilters.Match(paymentMethodId),
            Addresses = matchedPaymentMethodConfig.Addresses.Select(s =>
                new TronUSDtPaymentMethodInformation.TronUSDtPaymentMethodAddressInformation()
                {
                    Available = reservedAddresses.Contains(s) == false,
                    Balance = balances.Single(x => x.Item1 == s).Item2 == null
                        ? null
                        : balances.Single(x => x.Item1 == s).Item2!.Value,
                    Value = s
                }).ToArray()
        };

        return Ok(data);
    }

    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [HttpPost("~/api/v1/stores/{storeId}/tronUSDtlike/{paymentMethodId}/addresses")]
    public IActionResult AddAddress(PaymentMethodId paymentMethodId, [FromBody] TronUSDtAddAddressRequest request)
    {
        if (!pluginConfiguration.TronUSDtLikeConfigurationItems.ContainsKey(paymentMethodId))
            return NotFound();

        if (!TronUSDtAddressHelper.IsValid(request.Address))
            return BadRequest(new { message = "Invalid Tron address." });

        var address = request.Address;
        var store = StoreData;
        var currentConfig = store.GetPaymentMethodConfig<TronUSDtPaymentMethodConfig>(paymentMethodId, handlers)
                            ?? new TronUSDtPaymentMethodConfig();

        if (currentConfig.Addresses.Contains(address))
            return BadRequest(new { message = "Address already exists." });

        currentConfig.Addresses = [.. currentConfig.Addresses, address];
        store.SetPaymentMethodConfig(handlers[paymentMethodId], currentConfig);
        return Ok();
    }

    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [HttpDelete("~/api/v1/stores/{storeId}/tronUSDtlike/{paymentMethodId}/addresses/{address}")]
    public IActionResult DeleteAddress(PaymentMethodId paymentMethodId, string address)
    {
        if (!pluginConfiguration.TronUSDtLikeConfigurationItems.ContainsKey(paymentMethodId))
            return NotFound();

        var store = StoreData;
        var currentConfig = store.GetPaymentMethodConfig<TronUSDtPaymentMethodConfig>(paymentMethodId, handlers);

        if (currentConfig == null || !currentConfig.Addresses.Contains(address))
            return NotFound();

        currentConfig.Addresses = currentConfig.Addresses.Where(a => a != address).ToArray();
        store.SetPaymentMethodConfig(handlers[paymentMethodId], currentConfig);
        return Ok();
    }
}