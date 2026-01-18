namespace BTCPayServer.Plugins.USDt.Services.Payments;

/// <summary>
/// Payment method details for EVM-based on-chain payments.
/// Currently empty but available for future extensions.
/// </summary>
public class EVMOnChainPaymentMethodDetails
{
}

// Backwards compatibility aliases
public class TronUSDtLikeOnChainPaymentMethodDetails : EVMOnChainPaymentMethodDetails
{
}

public class EthUSDtLikeOnChainPaymentMethodDetails : EVMOnChainPaymentMethodDetails
{
}
