using BTCPayServer.Plugins.USDt.Configuration;

namespace BTCPayServer.Plugins.USDt.Controllers.ViewModels;

public class EditEVMUSDtPaymentMethodViewModel
{
    public required string DisplayName { get; init; }
    public required string ChainDisplayName { get; init; }
    public string AddressPlaceholder { get; init; } = "0x742d35Cc6634C0532925a3b844Bc454e4438f44e";
    public string? Address { get; init; }
    public bool Enabled { get; init; }

    public EditEVMUSDtPaymentMethodAddressViewModel[] Addresses { get; init; } = [];

    public class EditEVMUSDtPaymentMethodAddressViewModel
    {
        public required string Value { get; init; }
        public bool Available { get; init; }
        public required string Balance { get; init; }
    }
}
