using System;
using System.Collections.Generic;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Plugins.USDt.Services;

public static class USDtListenerShared
{
    internal const int InitialRateLimitBackoffMs = 5_000;
    internal const int MaximumRateLimitBackoffMs = 60_000;

    public static readonly IReadOnlyList<InvoiceStatus> StatusToTrack =
    [
        InvoiceStatus.New,
        InvoiceStatus.Processing
    ];

    internal static TimeSpan GetBlockPollingDelay(double blockTimeSeconds)
    {
        return TimeSpan.FromSeconds(Math.Max(1, blockTimeSeconds));
    }

    internal static int CalculateRateLimitDelayMs(int backoffMs, double jitterUnit)
    {
        jitterUnit = Math.Clamp(jitterUnit, 0, 1);
        var jitterMultiplier = 1.0 + (jitterUnit * 0.2);
        return Math.Min(
            MaximumRateLimitBackoffMs,
            Math.Max(1, (int)Math.Round(backoffMs * jitterMultiplier)));
    }

    internal static int GetNextRateLimitBackoffMs(int backoffMs)
    {
        return Math.Min(backoffMs * 2, MaximumRateLimitBackoffMs);
    }

    internal static bool IsRateLimitException(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("429", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase))
                return true;

            var isForbidden = message.Contains("403", StringComparison.OrdinalIgnoreCase) ||
                              message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase);
            var describesRateLimit = message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                                     message.Contains("frequency limit", StringComparison.OrdinalIgnoreCase) ||
                                     message.Contains("quota", StringComparison.OrdinalIgnoreCase) ||
                                     message.Contains("too many requests", StringComparison.OrdinalIgnoreCase);
            if (isForbidden && describesRateLimit)
                return true;
        }

        return false;
    }
}
