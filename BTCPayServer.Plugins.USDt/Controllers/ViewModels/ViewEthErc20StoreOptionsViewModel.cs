using System.Collections.Generic;
using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.USDt.Controllers.ViewModels;

public class ViewEthErc20StoreOptionsViewModel
{
    public List<ViewEthErc20StoreOptionItemViewModel> Items { get; set; } = new();
}

public class ViewEthErc20StoreOptionItemViewModel
{
    public PaymentMethodId PaymentMethodId { get; set; } = null!;
    public string DisplayName { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string[] Addresses { get; set; } = [];
}
