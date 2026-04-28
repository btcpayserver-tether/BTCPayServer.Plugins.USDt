using System.Collections.Generic;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Plugins.USDt.Services;

public static class USDtListenerShared
{
    public static readonly IReadOnlyList<InvoiceStatus> StatusToTrack =
    [
        InvoiceStatus.New,
        InvoiceStatus.Processing
    ];
}