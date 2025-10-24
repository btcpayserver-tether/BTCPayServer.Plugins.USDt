using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Plugins.USDt.Services;

public class EthUSDtSyncStatus : SyncStatus, ISyncStatus
{
    public required EthUSDtRPCProvider.EthUSDtLikeSummary Summary { get; init; }

    public override bool Available => Summary?.RpcAvailable ?? false;
}