using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Plugins.USDt.Services;

public class EthErc20SyncSummaryProvider(EthErc20RPCProvider rpcProvider) : ISyncSummaryProvider
{
    public bool AllAvailable()
    {
        return rpcProvider.Summaries.All(pair => pair.Value.RpcAvailable);
    }

    public string Partial => "EthErc20/EthErc20SyncSummary";

    public IEnumerable<ISyncStatus> GetStatuses()
    {
        return rpcProvider.Summaries.Select(pair => new EthErc20SyncStatus
        {
            Summary = pair.Value,
            PaymentMethodId = pair.Key.ToString()
        });
    }
}
