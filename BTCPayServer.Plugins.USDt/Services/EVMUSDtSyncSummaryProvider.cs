using System.Collections.Generic;
using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.USDt.Services;

public class EVMUSDtSyncSummaryProvider(EVMUSDtRPCProvider rpcProvider) : USDtSyncSummaryProvider
{
    public string Partial => "EVMUSDtLike/EVMUSDtSyncSummary";

    protected override IEnumerable<KeyValuePair<PaymentMethodId, USDtRpcSummary>> GetSummaries()
    {
        return rpcProvider.Summaries;
    }
}