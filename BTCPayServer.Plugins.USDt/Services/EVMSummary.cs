using System;
using System.Numerics;

namespace BTCPayServer.Plugins.USDt.Services;

/// <summary>
/// Represents the sync status summary for an EVM-based blockchain.
/// Unified across Tron, Ethereum, and other EVM chains.
/// </summary>
public class EVMSummary
{
    public bool Synced { get; set; }
    public BigInteger LatestBlockOnNode { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool RpcAvailable { get; set; }
    public BigInteger HighestBlockOnChain { get; set; }
    public BigInteger LatestBlockScanned { get; set; }
    public bool Syncing { get; set; }
}
