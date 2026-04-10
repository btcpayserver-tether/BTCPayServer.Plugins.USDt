using System;

namespace BTCPayServer.Plugins.USDt.Configuration.EVM;

public record EVMUSDtLikeConfigurationItem(string Chain) : USDtPluginConfigurationItem, IUSDtRpcConfigurationItem
{
    public required Uri JsonRpcUri { get; init; }
    public required string SmartContractAddress { get; init; }
    public override string Chain { get; } = Chain;
    public override required string Currency { get; init; }
    public required string DisplayName { get; init; }
    public required int Divisibility { get; init; }
    public required string CryptoImagePath { get; init; }
    public required string BlockExplorerLink { get; init; }
    public required string[] DefaultRateRules { get; init; }
    public required string CurrencyDisplayName { get; init; }

    /// <summary>
    /// Average block time in seconds. Used to compute the sync lag threshold.
    /// Ethereum mainnet ≈ 12s, Polygon PoS ≈ 2s.
    /// </summary>
    public double BlockTimeSeconds { get; init; } = 12.0;

    /// <summary>
    /// EIP-155 chain ID. Used in EIP-681 payment links so wallets select the correct network.
    /// Ethereum mainnet = 1, Polygon = 137.
    /// </summary>
    public int ChainId { get; init; } = 1;
}
