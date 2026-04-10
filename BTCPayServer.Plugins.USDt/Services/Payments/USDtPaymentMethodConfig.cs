using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class USDtPaymentMethodConfig
{
    public string[] Addresses { get; set; } = [];

    public async Task<string?> GetOneNotReservedAddress(PaymentMethodId paymentMethodId,
        InvoiceRepository invoiceRepository,
        IEnumerable<InvoiceStatus> trackedStatuses)
    {
        var allReservedAddresses = await GetReservedAddresses(paymentMethodId, invoiceRepository, trackedStatuses);
        return Addresses.Except(allReservedAddresses).FirstOrDefault();
    }

    public static async Task<string[]> GetReservedAddresses(PaymentMethodId paymentMethodId,
        InvoiceRepository invoiceRepository,
        IEnumerable<InvoiceStatus> trackedStatuses)
    {
        var trackedStatusSet = trackedStatuses.ToHashSet();
        var pendingInvoices = (await invoiceRepository.GetMonitoredInvoices(paymentMethodId, true))
            .Where(i => trackedStatusSet.Contains(i.Status));
        return pendingInvoices
            .Select(i => i.GetPaymentPrompt(paymentMethodId)?.Destination)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToArray();
    }
}