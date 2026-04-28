using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.USDt.Services;

public abstract class USDtSummaryUpdaterHostedService(Logs logs) : IHostedService
{
    private CancellationTokenSource? _cts;

    protected abstract IEnumerable<PaymentMethodId> GetPaymentMethodIds();
    protected abstract Task UpdateSummary(PaymentMethodId paymentMethodId);
    protected abstract bool IsAvailable(PaymentMethodId paymentMethodId);
    protected abstract string DaemonName { get; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        foreach (var paymentMethodId in GetPaymentMethodIds())
            _ = StartLoop(_cts.Token, paymentMethodId);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task StartLoop(CancellationToken cancellation, PaymentMethodId paymentMethodId)
    {
        logs.PayServer.LogInformation("Starting listening {DaemonName} daemons ({PaymentMethodId})", DaemonName,
            paymentMethodId);
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    await UpdateSummary(paymentMethodId);
                    await Task.Delay(IsAvailable(paymentMethodId) ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(30),
                        cancellation);
                }
                catch (Exception ex) when (!cancellation.IsCancellationRequested)
                {
                    logs.PayServer.LogError(ex, "Unhandled exception in Summary updater ({PaymentMethodId})",
                        paymentMethodId);
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellation);
                }
            }
        }
        catch when (cancellation.IsCancellationRequested)
        {
        }
    }
}