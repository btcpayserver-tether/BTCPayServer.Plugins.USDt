using System;

namespace BTCPayServer.Plugins.USDt.Configuration;

/// <summary>
/// Common interface for EVM-based blockchain configuration items (Tron, Ethereum, etc.)
/// </summary>
public interface IEVMConfigurationItem
{
    string Chain { get; }
    string Currency { get; }
    Uri JsonRpcUri { get; }
    string SmartContractAddress { get; }
    string DisplayName { get; }
    int Divisibility { get; }
    string CryptoImagePath { get; }
    string BlockExplorerLink { get; }
    string[] DefaultRateRules { get; }
    string CurrencyDisplayName { get; }
}
