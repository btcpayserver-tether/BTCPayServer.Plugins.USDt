using BTCPayServer.Plugins.USDt.Configuration;

namespace BTCPayServer.Plugins.USDt.Controllers.ViewModels;

public class EditEthUSDtPaymentMethodViewModel
{
    public string? Address { get; init; }
    public bool Enabled { get; init; }

    public EditEthUSDtPaymentMethodAddressViewModel[] Addresses { get; init; } = [];

    public class EditEthUSDtPaymentMethodAddressViewModel
    {
        public required string Value { get; init; }
        public bool Available { get; init; }
        public required string Balance { get; init; }
    }
}
