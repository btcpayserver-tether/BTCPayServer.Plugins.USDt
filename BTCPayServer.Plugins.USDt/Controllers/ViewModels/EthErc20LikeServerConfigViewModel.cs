using System;
using BTCPayServer.Validation;

namespace BTCPayServer.Plugins.USDt.Controllers.ViewModels;

public class EthErc20LikeServerConfigViewModel
{
    // No checksum enforcement here; we normalize to lowercase elsewhere
    public string? SmartContractAddress { get; init; }
    public string? DefaultSmartContractAddress { get; set; }

    [Uri]
    public string? JsonRpcUri { get; init; }
    public Uri? DefaultJsonRpcUri { get; set; }
}
