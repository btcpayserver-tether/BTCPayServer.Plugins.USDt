using System.Collections.Generic;
using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.USDt.Services;

public class TronUSDtSyncSummaryProvider(TronUSDtRPCProvider tronUSDtRpcProvider) : USDtSyncSummaryProvider
{
    public override string Partial => "TronUSDtLike/TronUSDtSyncSummary";

    protected override IEnumerable<KeyValuePair<PaymentMethodId, USDtRpcSummary>> GetSummaries()
    {
        return tronUSDtRpcProvider.Summaries;
    }
}