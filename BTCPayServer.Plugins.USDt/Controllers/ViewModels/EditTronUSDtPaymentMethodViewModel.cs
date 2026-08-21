using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Configuration.Tron;
using BTCPayServer.Plugins.USDt.Services.Payments;

namespace BTCPayServer.Plugins.USDt.Controllers.ViewModels;

public class EditTronUSDtPaymentMethodViewModel
{
    [TronBase58]
    public string? Address { get; init; }
    public bool Enabled { get; init; }
    public bool ExcludeAmountFromPaymentLink { get; init; }
    public USDtPaymentLinkFormat PaymentLinkFormat { get; init; } = USDtPaymentLinkFormat.Standard;
    public string? PaymentLinkTemplate { get; init; }
    public string TemplatePreviewDestination { get; init; } = "TNPeeaaFB7K9cmo4uQpcU32zGK8G1NYqeL";
    public string TemplatePreviewAmount { get; init; } = "12.34";
    public string TemplatePreviewAmountUnits { get; init; } = "12340000";
    public string TemplatePreviewSmartContractAddress { get; init; } = string.Empty;

    public EditTronUSDtPaymentMethodAddressViewModel[] Addresses { get; init; } =
        [];

    public class EditTronUSDtPaymentMethodAddressViewModel
    {
        public required string Value { get; init; }
        public bool Available { get; init; }
        public required string Balance { get; init; }
    }
}
