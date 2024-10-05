using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.USDt.Services.Events;

public class TronUSDtDaemonStateChanged
{
    public required string CryptoCode { get; set; }
    public required TronUSDtRPCProvider.TronUSDtLikeSummary Summary { get; set; }
}