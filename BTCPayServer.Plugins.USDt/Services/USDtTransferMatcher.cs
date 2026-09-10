using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BTCPayServer.Plugins.USDt.Services;

// Both listeners supply hexadecimal addresses; TRON converts its stored Base58 addresses at the boundary.
internal static class USDtTransferMatcher
{
    internal static IReadOnlyCollection<TransferMatchSnapshot> ToTransferMatchSnapshots(
        IEnumerable<TransferLogSnapshot> changes,
        IEnumerable<string> destinationKeys,
        IEnumerable<ExistingTransferSnapshot>? existingTransfers = null)
    {
        var destinationKeySet = destinationKeys
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var existing = new Dictionary<string, ExistingTransferSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var payment in existingTransfers ?? [])
        {
            if (existing.TryGetValue(payment.TransactionId, out var duplicate))
            {
                // Identical repeated entries are harmless, but two distinct stored IDs
                // differing only in case cannot safely be treated as one DB payment.
                if (duplicate.TransactionId != payment.TransactionId || duplicate.Value != payment.Value ||
                    !string.Equals(duplicate.To, payment.To, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Conflicting stored USDt payment ID: {payment.TransactionId}.");
            }
            else
                existing.Add(payment.TransactionId, payment);
        }
        // New IDs must not depend on which invoice destinations remain tracked.
        // Only reuse a legacy ID when it is already stored for a matching log.
        return changes
            .Where(change => !change.Removed)
            .Select(change => change with { TransactionHash = change.TransactionHash.ToLowerInvariant() })
            .OrderBy(change => change.LogIndex)
            .GroupBy(change => change.TransactionHash)
            .SelectMany(group =>
            {
                if (group.Any(change => change.LogIndex is null || change.LogIndex < 0))
                    throw new InvalidOperationException("USDt transfer log has no valid log index.");
                var logs = group.DistinctBy(change => change.LogIndex).ToArray();
                if (group.Any(change => change != logs.Single(log => log.LogIndex == change.LogIndex)))
                    throw new InvalidOperationException("Conflicting USDt transfer logs have the same log index.");
                var legacyId = $"{logs[0].TransactionHash.Replace("0x", "")}-{logs[0].TransactionIndex}";
                var legacyIndex = -1;
                if (existing.TryGetValue(legacyId, out var previous))
                {
                    legacyIndex = Array.FindIndex(logs, log =>
                        string.Equals(log.To, previous.To, StringComparison.OrdinalIgnoreCase) && log.Value == previous.Value &&
                        !existing.ContainsKey($"{legacyId}-log-{log.LogIndex}"));
                    if (legacyIndex < 0)
                        throw new InvalidOperationException($"Stored USDt payment {previous.TransactionId} does not match the returned transfer logs.");
                }
                return logs.Select((change, index) =>
                {
                    var id = index == legacyIndex ? previous!.TransactionId : $"{legacyId}-log-{change.LogIndex}";
                    if (existing.TryGetValue(id, out var stored))
                    {
                        if (stored.Value != change.Value || !string.Equals(stored.To, change.To, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"Stored USDt payment {stored.TransactionId} does not match the returned transfer log.");
                        id = stored.TransactionId;
                    }
                    return new TransferMatchSnapshot(change.To.ToLowerInvariant(), change.From, change.To, change.Value, id);
                });
            })
            .Where(match => destinationKeySet.Contains(match.DestinationKey))
            .DistinctBy(match => match.TransactionId)
            .ToArray();
    }

    internal sealed record TransferLogSnapshot(
        string To,
        string From,
        BigInteger Value,
        string TransactionHash,
        string TransactionIndex,
        bool Removed,
        BigInteger? LogIndex = null);

    internal sealed record ExistingTransferSnapshot(string TransactionId, string To, BigInteger Value);

    internal sealed record TransferMatchSnapshot(
        string DestinationKey,
        string From,
        string To,
        BigInteger TotalAmount,
        string TransactionId);
}
