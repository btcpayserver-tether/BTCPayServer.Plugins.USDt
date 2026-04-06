namespace BTCPayServer.Plugins.USDt.Controllers.Models;

public class EthUSDtPaymentMethodInformation
{
    public required string StoreId { get; init; }
    public required string PaymentMethodId { get; init; }
    public required bool Enabled { get; init; }

    public EthUSDtPaymentMethodAddressInformation[] Addresses { get; init; } = [];

    public class EthUSDtPaymentMethodAddressInformation
    {
        public required string Value { get; init; }
        public bool Available { get; init; }
        public required decimal? Balance { get; init; }
    }
}

public class EthUSDtAddAddressRequest
{
    public required string[] Addresses { get; init; }
}
