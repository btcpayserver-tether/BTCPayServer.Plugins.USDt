using System.Globalization;
using System.Numerics;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Nethereum.Web3;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class TronUSDtLikePaymentData
{
    public int ConfirmationCount { get; set; }
    public required string TransactionId { get; init; }
    public BigInteger BlockHeight { get; init; }
    public required string To { get; init; } // For future usages
    public required string From { get; init; } // For future usages

    public bool PaymentConfirmed(SpeedPolicy speedPolicy)
    {
        return speedPolicy switch
        {
            SpeedPolicy.HighSpeed => ConfirmationCount >= 2,
            SpeedPolicy.MediumSpeed => ConfirmationCount >= 6,
            SpeedPolicy.LowMediumSpeed => ConfirmationCount >= 12,
            SpeedPolicy.LowSpeed => ConfirmationCount >= 20,
            _ => false
        };
    }
}