using System;

namespace BTCPayServer.Plugins.USDt.Configuration.Ethereum;

public record EthUSDtLikeConfigurationItem(string Chain) : USDtPluginConfigurationItem
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
}
