using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.USDt.Services.Events;

public class EthErc20DaemonStateChanged
{
    public required PaymentMethodId PaymentMethodId { get; set; }
    public required EthErc20RPCProvider.Erc20LikeSummary Summary { get; set; }
}
