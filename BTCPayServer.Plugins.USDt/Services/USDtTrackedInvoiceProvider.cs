using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Plugins.USDt.Services;

public sealed class USDtTrackedInvoiceProvider
{
    internal static readonly TimeSpan BootstrapLookback = TimeSpan.FromDays(49);
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private readonly IUSDtInvoiceSource _invoiceSource;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<PaymentMethodId, TrackingState> _states = new();

    public USDtTrackedInvoiceProvider(InvoiceRepository invoiceRepository)
        : this(new InvoiceRepositoryUSDtInvoiceSource(invoiceRepository), TimeProvider.System)
    {
    }

    internal USDtTrackedInvoiceProvider(IUSDtInvoiceSource invoiceSource, TimeProvider timeProvider)
    {
        _invoiceSource = invoiceSource;
        _timeProvider = timeProvider;
    }

    public async Task<InvoiceEntity[]> GetTrackedInvoices(
        PaymentMethodId paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var state = _states.GetOrAdd(paymentMethodId, _ => new TrackingState());
        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            var now = _timeProvider.GetUtcNow();

            if (!state.Bootstrapped)
            {
                var expiredInvoices = await _invoiceSource.GetExpiredInvoicesSince(
                    now - BootstrapLookback,
                    cancellationToken);
                AddMatchingInvoices(state.Invoices, paymentMethodId, expiredInvoices);
                state.Bootstrapped = true;
                state.LastRefresh = now;
            }
            else if (now - state.LastRefresh >= RefreshInterval)
            {
                await RefreshKnownInvoices(state, cancellationToken);
                state.LastRefresh = now;
            }

            var monitoredInvoices = await _invoiceSource.GetMonitoredInvoices(paymentMethodId, cancellationToken);
            AddMatchingInvoices(state.Invoices, paymentMethodId, monitoredInvoices);

            foreach (var (invoiceId, invoice) in state.Invoices.ToArray())
            {
                if (!ShouldTrack(invoice, paymentMethodId, now))
                    state.Invoices.Remove(invoiceId);
            }

            return state.Invoices.Values.ToArray();
        }
        finally
        {
            state.Gate.Release();
        }
    }

    internal static bool ShouldTrack(
        InvoiceEntity invoice,
        PaymentMethodId paymentMethodId,
        DateTimeOffset now)
    {
        var prompt = invoice.GetPaymentPrompt(paymentMethodId);
        if (string.IsNullOrEmpty(prompt?.Destination))
            return false;

        if (USDtListenerShared.StatusToTrack.Contains(invoice.Status))
            return true;

        return invoice.Status == InvoiceStatus.Expired && invoice.MonitoringExpiration > now;
    }

    private async Task RefreshKnownInvoices(TrackingState state, CancellationToken cancellationToken)
    {
        var invoiceIds = state.Invoices.Keys.ToArray();
        if (invoiceIds.Length == 0)
            return;

        var refreshedInvoices = await _invoiceSource.GetInvoices(invoiceIds, cancellationToken);
        var refreshedById = refreshedInvoices.ToDictionary(invoice => invoice.Id);
        foreach (var invoiceId in invoiceIds)
        {
            if (refreshedById.TryGetValue(invoiceId, out var invoice))
                state.Invoices[invoiceId] = invoice;
            else
                state.Invoices.Remove(invoiceId);
        }
    }

    private static void AddMatchingInvoices(
        IDictionary<string, InvoiceEntity> destination,
        PaymentMethodId paymentMethodId,
        IEnumerable<InvoiceEntity> invoices)
    {
        foreach (var invoice in invoices)
        {
            if (!string.IsNullOrEmpty(invoice.GetPaymentPrompt(paymentMethodId)?.Destination))
                destination[invoice.Id] = invoice;
        }
    }

    private sealed class TrackingState
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public Dictionary<string, InvoiceEntity> Invoices { get; } = new();
        public bool Bootstrapped { get; set; }
        public DateTimeOffset LastRefresh { get; set; }
    }
}

internal interface IUSDtInvoiceSource
{
    Task<InvoiceEntity[]> GetMonitoredInvoices(
        PaymentMethodId paymentMethodId,
        CancellationToken cancellationToken);

    Task<InvoiceEntity[]> GetExpiredInvoicesSince(
        DateTimeOffset startDate,
        CancellationToken cancellationToken);

    Task<InvoiceEntity[]> GetInvoices(
        string[] invoiceIds,
        CancellationToken cancellationToken);
}

internal sealed class InvoiceRepositoryUSDtInvoiceSource(InvoiceRepository invoiceRepository) : IUSDtInvoiceSource
{
    public Task<InvoiceEntity[]> GetMonitoredInvoices(
        PaymentMethodId paymentMethodId,
        CancellationToken cancellationToken)
    {
        return invoiceRepository.GetMonitoredInvoices(paymentMethodId, true, cancellationToken);
    }

    public Task<InvoiceEntity[]> GetExpiredInvoicesSince(
        DateTimeOffset startDate,
        CancellationToken cancellationToken)
    {
        return invoiceRepository.GetInvoices(new InvoiceQuery
        {
            Status = [InvoiceStatus.Expired.ToString()],
            StartDate = startDate,
            IncludeArchived = false,
            OrderByDesc = false
        }, cancellationToken);
    }

    public Task<InvoiceEntity[]> GetInvoices(
        string[] invoiceIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return invoiceRepository.GetInvoices(invoiceIds);
    }
}
