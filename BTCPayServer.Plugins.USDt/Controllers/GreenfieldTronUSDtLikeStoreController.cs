using System.Linq;
using System.Threading.Tasks;
using AngleSharp.Text;
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

    private StoreData StoreData => HttpContext.GetStoreData();

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
}