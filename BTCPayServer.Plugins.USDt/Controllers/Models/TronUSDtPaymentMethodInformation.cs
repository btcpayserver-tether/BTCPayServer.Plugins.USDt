namespace BTCPayServer.Plugins.USDt.Controllers.Models;

public class TronUSDtPaymentMethodInformation
{
    public required string StoreId { get; init; }
    
    public required string PaymentMethodId { get; init; }
    public required bool Enabled { get; init; }

    public TronUSDtPaymentMethodAddressInformation[] Addresses { get; init; } =
        [];

    public class TronUSDtPaymentMethodAddressInformation
    {
        public required string Value { get; init; }
        public bool Available { get; init; }
        public required decimal? Balance { get; init; }
    }
}