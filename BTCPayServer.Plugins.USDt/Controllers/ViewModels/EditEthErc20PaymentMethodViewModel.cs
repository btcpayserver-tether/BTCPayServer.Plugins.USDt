using BTCPayServer.Plugins.USDt.Configuration;

namespace BTCPayServer.Plugins.USDt.Controllers.ViewModels;

public class EditEthErc20PaymentMethodViewModel
{
    public string? Address { get; init; }
    public bool Enabled { get; init; }

    public EditEthErc20PaymentMethodAddressViewModel[] Addresses { get; init; } = [];

    public class EditEthErc20PaymentMethodAddressViewModel
    {
        public required string Value { get; init; }
        public bool Available { get; init; }
        public required string Balance { get; init; }
    }
}
