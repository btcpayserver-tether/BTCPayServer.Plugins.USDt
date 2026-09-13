using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.USDt.Services;

internal static class USDtAddressPool
{
    // Validate the entire batch before any addresses are saved. EVM addresses
    // identify the same account regardless of casing; TRON Base58 is case-sensitive.
    internal static bool TryNormalize(IEnumerable<string?>? addresses, bool evm,
        out string[] normalized, out string? error)
    {
        normalized = [];
        error = null;
        if (addresses is null)
        {
            error = "Addresses must be an array.";
            return false;
        }

        var result = new List<string>();
        var seen = new HashSet<string>(evm ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var value in addresses)
        {
            var address = value?.Trim();
            if (!(evm ? EVMAddressHelper.IsValid(address) : TronUSDtAddressHelper.IsValid(address)))
            {
                error = $"Address {result.Count + 1} is not a valid {(evm ? "EVM" : "TRON")} address.";
                return false;
            }

            address = evm ? address!.ToLowerInvariant() : address!;
            if (!seen.Add(address))
            {
                error = $"Duplicate address: {address}. Remove duplicate entries and try again.";
                return false;
            }
            result.Add(address);
        }

        normalized = result.ToArray();
        return true;
    }
}
