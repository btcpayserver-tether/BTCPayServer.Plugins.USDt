using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class EthUSDtPaymentMethodConfig : USDtPaymentMethodConfig
{
    public async Task<string?> GetOneNotReservedAddress(PaymentMethodId paymentMethodId,
        InvoiceRepository invoiceRepository)
    {
        return await base.GetOneNotReservedAddress(paymentMethodId, invoiceRepository, EthUSDtListener.StatusToTrack);
    }

    public static async Task<string[]> GetReservedAddresses(PaymentMethodId paymentMethodId,
        InvoiceRepository invoiceRepository)
    {
        return await USDtPaymentMethodConfig.GetReservedAddresses(paymentMethodId, invoiceRepository,
            EthUSDtListener.StatusToTrack);
    }
}