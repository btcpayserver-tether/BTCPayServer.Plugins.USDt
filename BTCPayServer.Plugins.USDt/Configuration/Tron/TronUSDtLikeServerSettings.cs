using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.USDt.Configuration.Tron;

public class TronUSDtLikeServerSettings
{
    public Uri? JsonRpcUri { get; init; }
    public string? SmartContractAddress { get; init; }
    
    /// <summary>
    /// Custom HTTP headers to be sent with every RPC request.
    /// Useful for API authentication (e.g., TronGrid API key via TRON-PRO-API-KEY header).
    /// </summary>
    public Dictionary<string, string>? HttpHeaders { get; init; }
}