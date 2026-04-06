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

namespace BTCPayServer.Plugins.USDt.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
[EnableCors(CorsPolicies.All)]
public class GreenfieldEthUSDtLikeStoreController(
    EthUSDtRPCProvider ethUSDtRpcProvider,
    PaymentMethodHandlerDictionary handlers,
    InvoiceRepository invoiceRepository,
    USDtPluginConfiguration pluginConfiguration) : ControllerBase
{
    private StoreData StoreData => HttpContext.GetStoreDataOrNull()!;

    [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [HttpGet("~/api/v1/stores/{storeId}/evmUSDtlike/{paymentMethodId}")]
    public async Task<IActionResult> GetStoreInformation(PaymentMethodId paymentMethodId)
    {
        if (!pluginConfiguration.EVMUSDtLikeConfigurationItems.ContainsKey(paymentMethodId))
            return NotFound();

        var excludeFilters = StoreData.GetStoreBlob().GetExcludedPaymentMethods();
        var matchedPaymentMethodConfig =
            StoreData.GetPaymentMethodConfig<EthUSDtPaymentMethodConfig>(paymentMethodId, handlers);

        if (matchedPaymentMethodConfig == null)
            return NotFound();

        var balances =
            await ethUSDtRpcProvider.GetBalances(paymentMethodId, [.. matchedPaymentMethodConfig.Addresses]);
        var reservedAddresses =
            await EthUSDtPaymentMethodConfig.GetReservedAddresses(paymentMethodId, invoiceRepository);

        return Ok(new EthUSDtPaymentMethodInformation
        {
            StoreId = StoreData.Id,
            PaymentMethodId = paymentMethodId.ToString(),
            Enabled = !excludeFilters.Match(paymentMethodId),
            Addresses = matchedPaymentMethodConfig.Addresses.Select(s =>
                new EthUSDtPaymentMethodInformation.EthUSDtPaymentMethodAddressInformation
                {
                    Available = !reservedAddresses.Contains(s),
                    Balance = balances.Single(x => x.Item1 == s).Item2,
                    Value = s
                }).ToArray()
        });
    }

    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [HttpPost("~/api/v1/stores/{storeId}/evmUSDtlike/{paymentMethodId}/addresses")]
    public IActionResult AddAddress(PaymentMethodId paymentMethodId, [FromBody] EthUSDtAddAddressRequest request)
    {
        if (!pluginConfiguration.EVMUSDtLikeConfigurationItems.ContainsKey(paymentMethodId))
            return NotFound();

        if (!EthAddressHelper.IsValid(request.Address))
            return BadRequest(new { message = "Invalid EVM address." });

        var address = request.Address.ToLowerInvariant();
        var store = StoreData;
        var currentConfig = store.GetPaymentMethodConfig<EthUSDtPaymentMethodConfig>(paymentMethodId, handlers)
                            ?? new EthUSDtPaymentMethodConfig();

        if (currentConfig.Addresses.Contains(address))
            return BadRequest(new { message = "Address already exists." });

        currentConfig.Addresses = [.. currentConfig.Addresses, address];
        store.SetPaymentMethodConfig(handlers[paymentMethodId], currentConfig);
        return Ok();
    }

    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [HttpDelete("~/api/v1/stores/{storeId}/evmUSDtlike/{paymentMethodId}/addresses/{address}")]
    public IActionResult DeleteAddress(PaymentMethodId paymentMethodId, string address)
    {
        if (!pluginConfiguration.EVMUSDtLikeConfigurationItems.ContainsKey(paymentMethodId))
            return NotFound();

        var store = StoreData;
        var currentConfig = store.GetPaymentMethodConfig<EthUSDtPaymentMethodConfig>(paymentMethodId, handlers);
        var normalized = address.ToLowerInvariant();

        if (currentConfig == null || !currentConfig.Addresses.Contains(normalized))
            return NotFound();

        currentConfig.Addresses = currentConfig.Addresses.Where(a => a != normalized).ToArray();
        store.SetPaymentMethodConfig(handlers[paymentMethodId], currentConfig);
        return Ok();
    }
}
