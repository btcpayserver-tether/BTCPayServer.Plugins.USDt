using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Plugins.TronUSDT.Services;

public class TronUSDTSyncStatus : SyncStatus, ISyncStatus
{
    public TronUSDTRPCProvider.TronUSDTLikeSummary Summary { get; set; }

    public override bool Available => Summary?.RpcAvailable ?? false;
}