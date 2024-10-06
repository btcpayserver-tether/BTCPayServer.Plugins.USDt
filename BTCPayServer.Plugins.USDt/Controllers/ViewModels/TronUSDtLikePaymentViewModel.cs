using System;

namespace BTCPayServer.Plugins.USDt.Controllers.ViewModels;

public class TronUSDtLikePaymentViewModel
{
    public required string CurrencyDisplayName { get; init; }
    public required string Confirmations { get; init; }
    public required string DepositAddress { get; init; }
    public required string Amount { get; init; }
    public required string TransactionId { get; init; }
    public DateTimeOffset ReceivedTime { get; set; }
    public required string? TransactionLink { get; init; }
}