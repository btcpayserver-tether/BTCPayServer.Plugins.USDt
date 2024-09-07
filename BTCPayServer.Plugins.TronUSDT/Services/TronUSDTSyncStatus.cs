using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Plugins.TronUSDT.Services;

public class TronUSDTSyncStatus : SyncStatus, ISyncStatus
{
    public required TronUSDTRPCProvider.TronUSDTLikeSummary Summary { get; init; }

    public override bool Available => Summary?.RpcAvailable ?? false;
}