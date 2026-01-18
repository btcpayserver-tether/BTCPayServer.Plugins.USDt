using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

/// <summary>
/// Base class for EVM-based payment method configuration (Tron, Ethereum, etc.)
/// Handles address management and reservation logic.
/// </summary>
public abstract class EVMPaymentMethodConfig
{
    public string[] Addresses { get; set; } = [];

    /// <summary>
    /// Invoice statuses to track for address reservation
    /// </summary>
    protected static readonly List<InvoiceStatus> StatusToTrack =
    [
        InvoiceStatus.New,
        InvoiceStatus.Processing
    ];

    public async Task<string?> GetOneNotReservedAddress(PaymentMethodId paymentMethodId,
        InvoiceRepository invoiceRepository)
    {
        var allReservedAddresses = await GetReservedAddresses(paymentMethodId, invoiceRepository);
        return Addresses.Except(allReservedAddresses).FirstOrDefault();
    }

    public static async Task<string[]> GetReservedAddresses(PaymentMethodId paymentMethodId,
        InvoiceRepository invoiceRepository)
    {
        var pendingInvoices = (await invoiceRepository.GetMonitoredInvoices(paymentMethodId, true))
            .Where(i => StatusToTrack.Contains(i.Status));
        return pendingInvoices
            .Select(i => i.GetPaymentPrompt(paymentMethodId)?.Destination)
            .Where(s => s is not null)
            .Select(s => s!).ToArray();
    }
}

/// <summary>
/// Tron-specific payment method configuration
/// </summary>
public class TronUSDtPaymentMethodConfig : EVMPaymentMethodConfig
{
}

/// <summary>
/// Ethereum-specific payment method configuration
/// </summary>
public class EthUSDtPaymentMethodConfig : EVMPaymentMethodConfig
{
}
