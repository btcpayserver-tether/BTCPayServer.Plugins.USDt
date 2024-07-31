namespace BTCPayServer.Plugins.TronUSDT.Services.Events;

public class TronUSDTDaemonStateChanged
{
    public required string CryptoCode { get; set; }
    public required TronUSDTRPCProvider.TronUSDTLikeSummary Summary { get; set; }
}