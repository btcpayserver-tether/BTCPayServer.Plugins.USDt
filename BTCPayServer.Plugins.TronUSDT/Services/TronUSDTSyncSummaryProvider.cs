using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Plugins.TronUSDT.Services;

public class TronUSDTSyncSummaryProvider(TronUSDTRPCProvider tronUSDTRpcProvider) : ISyncSummaryProvider
{
    public bool AllAvailable()
    {
        return tronUSDTRpcProvider.Summaries.All(pair => pair.Value.RpcAvailable);
    }

    public string Partial => "TronUSDT/TronUSDTSyncSummary";

    public IEnumerable<ISyncStatus> GetStatuses()
    {
        return tronUSDTRpcProvider.Summaries.Select(pair => new TronUSDTSyncStatus
        {
            Summary = pair.Value,
            CryptoCode = pair.Key
        });
    }
}