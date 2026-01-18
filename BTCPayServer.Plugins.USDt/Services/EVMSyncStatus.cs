using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Plugins.USDt.Services;

/// <summary>
/// Base sync status for EVM-based blockchains
/// </summary>
public class EVMSyncStatus : SyncStatus, ISyncStatus
{
    public required EVMSummary Summary { get; init; }

    public override bool Available => Summary?.RpcAvailable ?? false;
}
