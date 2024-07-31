namespace BTCPayServer.Plugins.TronUSDT.Controllers.ViewModels;

public class EditTronUSDTPaymentMethodViewModel
{
    public string? Address { get; init; }
    public bool Enabled { get; init; }

    public EditTronUSDTPaymentMethodAddressViewModel[] Addresses { get; init; } =
        [];

    public class EditTronUSDTPaymentMethodAddressViewModel
    {
        public required string Value { get; init; }
        public bool Available { get; init; }
        public required string Balance { get; init; }
    }
}