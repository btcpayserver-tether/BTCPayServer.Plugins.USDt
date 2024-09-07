using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.TronUSDT.Services.Payments;

public class TronUSDTPaymentModelExtension(
    PaymentMethodId paymentMethodId,
    BTCPayNetworkBase network) : IPaymentModelExtension
{
    public PaymentMethodId PaymentMethodId { get; } = paymentMethodId;

    public string DisplayName => network.DisplayName;
    public string Image => network.CryptoImagePath;
    public string Badge => "";

    public void ModifyPaymentModel(PaymentModelContext context)
    {
        if (context.Model.Activated)
        {
            context.Model.InvoiceBitcoinUrl = context.Prompt.Destination;
            context.Model.InvoiceBitcoinUrlQR = context.Model.InvoiceBitcoinUrl;
            context.Model.ShowPayInWalletButton = false;
        }
        else
        {
            context.Model.InvoiceBitcoinUrl = "";
            context.Model.InvoiceBitcoinUrlQR = "";
            context.Model.ShowPayInWalletButton = false;
        }
    }
}