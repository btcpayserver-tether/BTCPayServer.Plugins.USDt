using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;

namespace BTCPayServer.Plugins.USDt.Services;

public class EVMUSDtLikeSummaryUpdaterHostedService(
    EVMUSDtRPCProvider rpcProvider,
    USDtPluginConfiguration usdtPluginConfiguration,
    Logs logs)
    : USDtSummaryUpdaterHostedService(logs)
{
    protected override IEnumerable<PaymentMethodId> GetPaymentMethodIds()
    {
        return usdtPluginConfiguration.EVMUSDtLikeConfigurationItems.Keys;
    }

    protected override Task UpdateSummary(PaymentMethodId paymentMethodId)
    {
        return rpcProvider.UpdateSummary(paymentMethodId);
    }

    protected override bool IsAvailable(PaymentMethodId paymentMethodId)
    {
        return rpcProvider.IsAvailable(paymentMethodId);
    }

    protected override string DaemonName => "EVM USDTLike";
}