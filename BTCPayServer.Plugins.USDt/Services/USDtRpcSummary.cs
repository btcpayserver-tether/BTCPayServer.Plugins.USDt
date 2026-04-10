using System;
using System.Numerics;

namespace BTCPayServer.Plugins.USDt.Services;

public class USDtRpcSummary
{
    public bool Synced { get; set; }
    public BigInteger LatestBlockOnNode { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool RpcAvailable { get; set; }
    public BigInteger HighestBlockOnChain { get; set; }
    public BigInteger LatestBlockScanned { get; set; }
    public bool Syncing { get; set; }
}