#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Plugins.TronUSDT.Services.Payments;

public class TronUSDTPaymentMethodConfig
{
    public string[] Addresses { get; set; } = Array.Empty<string>();

    public async Task<string?> GetOneNotReservedAddress(PaymentMethodId paymentMethodId,
        InvoiceRepository invoiceRepository)
    {
        var allReservedAddresses = await GetReservedAddresses(paymentMethodId, invoiceRepository);
        return Addresses.Except(allReservedAddresses).FirstOrDefault();
    }

    public async Task<string[]> GetReservedAddresses(PaymentMethodId paymentMethodId,
        InvoiceRepository invoiceRepository)
    {
        var pendingInvoices = (await invoiceRepository.GetPendingInvoices()).Where(i => TronUSDTListener.StatusToTrack.Contains(i.Status));
        return pendingInvoices
            .Select(i => i.GetPaymentPrompt(paymentMethodId)?.Destination)
            .Where(s => s is not null)
            .Select(s => s!).ToArray();
    }
}