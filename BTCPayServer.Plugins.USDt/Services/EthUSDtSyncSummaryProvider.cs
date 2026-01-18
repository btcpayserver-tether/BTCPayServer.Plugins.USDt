using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Plugins.USDt.Services;

public class EthUSDtSyncSummaryProvider(EthUSDtRPCProvider rpcProvider) : ISyncSummaryProvider
{
    public bool AllAvailable()
    {
        return rpcProvider.Summaries.All(pair => pair.Value.RpcAvailable);
    }

    public string Partial => "EthUSDtLike/EthUSDtSyncSummary";

    public IEnumerable<ISyncStatus> GetStatuses()
    {
        return rpcProvider.Summaries.Select(pair => new EVMSyncStatus
        {
            Summary = pair.Value,
            PaymentMethodId = pair.Key.ToString()
        });
    }
}
