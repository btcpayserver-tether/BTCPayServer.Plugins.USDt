using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.USDt.Services.Events;

public class USDtDaemonStateChanged
{
    public required PaymentMethodId PaymentMethodId { get; set; }
    public required USDtRpcSummary Summary { get; set; }
}