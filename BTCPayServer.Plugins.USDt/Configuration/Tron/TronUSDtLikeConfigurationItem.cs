using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.USDt.Configuration.Tron;

public record TronUSDtLikeConfigurationItem : USDtPluginConfigurationItem, IEVMConfigurationItem
{
    public override string Chain => Constants.TronChainName;

    public required Uri JsonRpcUri { get; init; }
    public required string SmartContractAddress { get; init; }
    public override required string Currency { get; init; }
    public required string DisplayName { get; init; }
    public required int Divisibility { get; init; }
    public required string CryptoImagePath { get; init; }
    public required string BlockExplorerLink { get; init; }
    public required string[] DefaultRateRules { get; init; }
    public required string CurrencyDisplayName { get; init; }
    
    /// <summary>
    /// Custom HTTP headers to be sent with every RPC request.
    /// Useful for API authentication (e.g., TronGrid API key via TRON-PRO-API-KEY header).
    /// </summary>
    public Dictionary<string, string>? HttpHeaders { get; init; }
}