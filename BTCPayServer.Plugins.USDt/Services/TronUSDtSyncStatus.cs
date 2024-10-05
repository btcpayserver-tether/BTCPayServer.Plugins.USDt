using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Plugins.USDt.Services;

public class TronUSDtSyncStatus : SyncStatus, ISyncStatus
{
    public required TronUSDtRPCProvider.TronUSDtLikeSummary Summary { get; init; }

    public override bool Available => Summary?.RpcAvailable ?? false;
}