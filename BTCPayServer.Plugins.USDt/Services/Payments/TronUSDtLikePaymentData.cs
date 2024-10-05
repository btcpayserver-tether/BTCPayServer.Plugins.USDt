using System.Globalization;
using System.Numerics;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Invoices;
using Nethereum.Web3;

namespace BTCPayServer.Plugins.TronUSDt.Services.Payments;

public class TronUSDtLikePaymentData : CryptoPaymentData
{
    public BigInteger Amount { get; init; }
    public int ConfirmationCount { get; set; }
    public required string TransactionId { get; init; }
    public BigInteger BlockHeight { get; init; }
    public required string To { get; init; } // For future usages
    public required string From { get; init; } // For future usages
    public required string CryptoCode { get; init; }

    public string GetPaymentProof()
    {
#pragma warning disable CS8603 // Possible null reference return.
        return null;
#pragma warning restore CS8603 // Possible null reference return.
    }

    public decimal GetValue()
    {
        return decimal.Parse(Web3.Convert.FromWeiToBigDecimal(Amount, 6).ToString(), //todo vbn fix constant
            CultureInfo.InvariantCulture);
    }


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