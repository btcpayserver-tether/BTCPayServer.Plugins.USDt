using System;

namespace BTCPayServer.Plugins.USDt.Configuration;

public interface IUSDtRpcConfigurationItem
{
    Uri JsonRpcUri { get; }
    string SmartContractAddress { get; }
    int Divisibility { get; }
    double BlockTimeSeconds { get; }
}