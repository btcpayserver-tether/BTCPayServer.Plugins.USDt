namespace BTCPayServer.Plugins.TronUSDt.Services.Events;

public class TronUSDtDaemonStateChanged
{
    public required string CryptoCode { get; set; }
    public required TronUSDtRPCProvider.TronUSDtLikeSummary Summary { get; set; }
}