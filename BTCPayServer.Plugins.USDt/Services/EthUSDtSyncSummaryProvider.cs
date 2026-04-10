using System.Collections.Generic;
using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.USDt.Services;

public class EthUSDtSyncSummaryProvider(EthUSDtRPCProvider rpcProvider) : USDtSyncSummaryProvider
{
    public string Partial => "EthUSDtLike/EthUSDtSyncSummary";

    protected override IEnumerable<KeyValuePair<PaymentMethodId, USDtRpcSummary>> GetSummaries()
    {
        return rpcProvider.Summaries;
    }
}