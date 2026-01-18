using System.Globalization;
using BTCPayServer.Services;

namespace BTCPayServer.Plugins.USDt.Services;

/// <summary>
/// Transaction link provider for EVM-based blockchains.
/// Extracts the transaction hash from the payment ID (format: {txHash}-{logIndex})
/// </summary>
internal class EVMTransactionLinkProvider(string blockExplorerLink)
    : DefaultTransactionLinkProvider(blockExplorerLink)
{
    public override string? GetTransactionLink(string paymentId)
    {
        if (string.IsNullOrEmpty(BlockExplorerLink))
            return null;
        return string.Format(CultureInfo.InvariantCulture, BlockExplorerLink, paymentId.Split('-')[0]);
    }
}

// Backwards compatibility aliases
internal class TronUSDtTransactionLinkProvider(string blockExplorerLink) 
    : EVMTransactionLinkProvider(blockExplorerLink);

internal class EthUSDtTransactionLinkProvider(string blockExplorerLink) 
    : EVMTransactionLinkProvider(blockExplorerLink);
