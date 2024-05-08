using System.Globalization;
using System.Numerics;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Invoices;
using Nethereum.Web3;

namespace BTCPayServer.Plugins.TronUSDT.Services.Payments;

public class TronUSDTLikePaymentData : CryptoPaymentData
{
    public BigInteger Amount { get; set; }
    public string CryptoCode { get; set; }
    public string From { get; set; }
    public string To { get; set; }

    public int ConfirmationCount { get; set; }

    // public BTCPayNetworkBase Network { get; set; }
    public string TransactionId { get; set; }
    public BigInteger BlockHeight { get; set; }

    public string GetPaymentProof()
    {
        return null;
    }

    public string GetPaymentId()
    {
        return TransactionId;
    }

    public string[] GetSearchTerms()
    {
        return new[] { From };
    }

    public decimal GetValue()
    {
        return decimal.Parse(Web3.Convert.FromWeiToBigDecimal(Amount, 6).ToString(), //todo vbn fix constant
            CultureInfo.InvariantCulture);
    }


    public bool PaymentConfirmed(SpeedPolicy speedPolicy)
    {
        switch (speedPolicy)
        {
            case SpeedPolicy.HighSpeed:
                return ConfirmationCount >= 2;
            case SpeedPolicy.MediumSpeed:
                return ConfirmationCount >= 6;
            case SpeedPolicy.LowMediumSpeed:
                return ConfirmationCount >= 12;
            case SpeedPolicy.LowSpeed:
                return ConfirmationCount >= 20;
            default:
                return false;
        }
    }

    public string GetDestination()
    {
        return To;
    }
}