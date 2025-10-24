using Nethereum.Util;

namespace BTCPayServer.Plugins.USDt.Services;

public static class EthAddressHelper
{
    public static bool IsValid(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        // quick format check
        if (!AddressUtil.Current.IsValidEthereumAddressHexFormat(address)) return false;
        return true;
    }
}