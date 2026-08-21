using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class USDtPaymentMethodConfig
{
    public string[] Addresses { get; set; } = [];
    public bool Activated { get; set; }

    public void MarkActivated()
    {
        Activated = true;
    }

    public void PreserveActivationFrom(USDtPaymentMethodConfig? previousConfig)
    {
        if (WasConfiguredOrActivated() || previousConfig?.WasConfiguredOrActivated() is true)
            MarkActivated();
    }

    public bool ActivatesChain(bool excluded)
    {
        return WasConfiguredOrActivated() || !excluded;
    }

    private bool WasConfiguredOrActivated()
    {
        return Activated || Addresses is { Length: > 0 };
    }

    public async Task<string?> GetOneNotReservedAddress(PaymentMethodId paymentMethodId,
        USDtTrackedInvoiceProvider trackedInvoiceProvider)
    {
        var allReservedAddresses = await GetReservedAddresses(paymentMethodId, trackedInvoiceProvider);
        return Addresses.Except(allReservedAddresses).FirstOrDefault();
    }

    public static async Task<string[]> GetReservedAddresses(PaymentMethodId paymentMethodId,
        USDtTrackedInvoiceProvider trackedInvoiceProvider)
    {
        var trackedInvoices = await trackedInvoiceProvider.GetTrackedInvoices(paymentMethodId);
        return trackedInvoices
            .Select(i => i.GetPaymentPrompt(paymentMethodId)?.Destination)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToArray();
    }
}
