using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.USDt.Controllers.ViewModels;

public class ViewUSDtStoreOptionItemViewModel
{
    public required string DisplayName { get; init; }
    public bool Enabled { get; init; }
    public required PaymentMethodId PaymentMethodId { get; init; }
    public required string[] Addresses { get; init; }
}