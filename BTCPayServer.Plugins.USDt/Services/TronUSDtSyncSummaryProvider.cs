using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Plugins.USDt.Services;

public class TronUSDtSyncSummaryProvider(TronUSDtRPCProvider tronUSDtRpcProvider) : ISyncSummaryProvider
{
    public bool AllAvailable()
    {
        return tronUSDtRpcProvider.Summaries.All(pair => pair.Value.RpcAvailable);
    }

    public string Partial => "TronUSDt/TronUSDtSyncSummary";

    public IEnumerable<ISyncStatus> GetStatuses()
    {
        return tronUSDtRpcProvider.Summaries.Select(pair => new TronUSDtSyncStatus
        {
            Summary = pair.Value,
            CryptoCode = pair.Key
        });
    }
}