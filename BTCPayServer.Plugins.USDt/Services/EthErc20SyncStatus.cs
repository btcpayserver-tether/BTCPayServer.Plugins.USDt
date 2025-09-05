using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Plugins.USDt.Services;

public class EthErc20SyncStatus : SyncStatus, ISyncStatus
{
    public required EthErc20RPCProvider.Erc20LikeSummary Summary { get; init; }

    public override bool Available => Summary?.RpcAvailable ?? false;
}
