using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class TronUSDtPaymentMethodConfig : USDtPaymentMethodConfig
{
    public bool ExcludeAmountFromPaymentLink { get; set; }

    public async Task<string?> GetOneNotReservedAddress(PaymentMethodId paymentMethodId,
        InvoiceRepository invoiceRepository)
    {
        return await base.GetOneNotReservedAddress(paymentMethodId, invoiceRepository, USDtListenerShared.StatusToTrack);
    }

    public static async Task<string[]> GetReservedAddresses(PaymentMethodId paymentMethodId,
        InvoiceRepository invoiceRepository)
    {
        return await USDtPaymentMethodConfig.GetReservedAddresses(paymentMethodId, invoiceRepository,
            USDtListenerShared.StatusToTrack);
    }
}