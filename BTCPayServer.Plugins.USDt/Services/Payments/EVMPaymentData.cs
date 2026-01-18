using System.Numerics;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

/// <summary>
/// Base class for EVM-based payment data (Tron, Ethereum, etc.)
/// Contains common fields and confirmation logic for EVM chains.
/// </summary>
public abstract class EVMPaymentData
{
    public int ConfirmationCount { get; set; }
    public required string TransactionId { get; init; }
    public BigInteger BlockHeight { get; init; }
    public required string To { get; init; }
    public required string From { get; init; }

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

/// <summary>
/// Tron-specific payment data (inherits common EVM behavior)
/// </summary>
public class TronUSDtLikePaymentData : EVMPaymentData
{
}

/// <summary>
/// Ethereum-specific payment data (inherits common EVM behavior)
/// </summary>
public class EthUSDtPaymentData : EVMPaymentData
{
}
