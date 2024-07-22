using System;

namespace BTCPayServer.Plugins.TronUSDT.ViewModels;

public class TronUSDTPaymentViewModel
{
    public required string Crypto { get; init; }
    public required string Confirmations { get; set; }
    public required string DepositAddress { get; init; }
    public required string Amount { get; init; }
    public required string TransactionId { get; init; }
    public DateTimeOffset ReceivedTime { get; set; }
    public required string? TransactionLink { get; init; }
}