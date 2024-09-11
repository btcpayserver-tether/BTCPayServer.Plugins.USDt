using BTCPayServer.Plugins.TronUSDt.Configuration;

namespace BTCPayServer.Plugins.TronUSDt.Controllers.ViewModels;

public class EditTronUSDtPaymentMethodViewModel
{
    [TronBase58]
    public string? Address { get; init; }
    public bool Enabled { get; init; }

    public EditTronUSDtPaymentMethodAddressViewModel[] Addresses { get; init; } =
        [];

    public class EditTronUSDtPaymentMethodAddressViewModel
    {
        public required string Value { get; init; }
        public bool Available { get; init; }
        public required string Balance { get; init; }
    }
}