using System;

namespace BTCPayServer.Plugins.USDt.Services.Payments;

public class EVMUSDtPaymentMethodConfig : USDtPaymentMethodConfig
{
    protected override StringComparer AddressComparer => StringComparer.OrdinalIgnoreCase;
    public USDtPaymentLinkFormat? PaymentLinkFormat { get; set; }
    public string? PaymentLinkTemplate { get; set; }
}
