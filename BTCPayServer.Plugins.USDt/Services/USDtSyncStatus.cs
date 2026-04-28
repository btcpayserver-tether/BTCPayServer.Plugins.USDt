using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Plugins.USDt.Services;

public class USDtSyncStatus : SyncStatus, ISyncStatus
{
    public required USDtRpcSummary Summary { get; init; }

    public override bool Available => Summary.RpcAvailable;
}