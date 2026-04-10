using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.USDt.Services;

public abstract class USDtSyncSummaryProvider : ISyncSummaryProvider
{
    protected abstract IEnumerable<KeyValuePair<PaymentMethodId, USDtRpcSummary>> GetSummaries();

    public bool AllAvailable()
    {
        return GetSummaries().All(pair => pair.Value.RpcAvailable);
    }

    public abstract string Partial { get; }

    public IEnumerable<ISyncStatus> GetStatuses()
    {
        return GetSummaries().Select(pair => new USDtSyncStatus
        {
            Summary = pair.Value,
            PaymentMethodId = pair.Key.ToString()
        });
    }
}