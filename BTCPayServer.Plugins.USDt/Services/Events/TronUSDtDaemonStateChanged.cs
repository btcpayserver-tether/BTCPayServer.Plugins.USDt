using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.USDt.Services.Events;

public class TronUSDtDaemonStateChanged
{
    public required PaymentMethodId PaymentMethodId { get; set; }
    public required EVMSummary Summary { get; set; }
}
