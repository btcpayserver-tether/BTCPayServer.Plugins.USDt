using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;

namespace BTCPayServer.Plugins.USDt.Services;

public class TronUSDtLikeSummaryUpdaterHostedService(
    TronUSDtRPCProvider tronUSDtRpcProvider,
    USDtPluginConfiguration usdtPluginConfiguration,
    Logs logs,
    USDtChainActivationService activationService)
    : USDtSummaryUpdaterHostedService(logs, activationService)
{
    protected override IEnumerable<PaymentMethodId> GetPaymentMethodIds()
    {
        return usdtPluginConfiguration.TronUSDtLikeConfigurationItems.Keys;
    }

    protected override Task UpdateSummary(PaymentMethodId paymentMethodId)
    {
        return tronUSDtRpcProvider.UpdateSummary(paymentMethodId);
    }

    protected override bool IsAvailable(PaymentMethodId paymentMethodId)
    {
        return tronUSDtRpcProvider.IsAvailable(paymentMethodId);
    }

    protected override string DaemonName => "TronUSDt-like";
}
