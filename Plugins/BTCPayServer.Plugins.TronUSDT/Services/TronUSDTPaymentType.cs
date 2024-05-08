namespace BTCPayServer.Plugins.TronUSDT.Tron.TronUSDT;

// public class TronUSDTPaymentType : PaymentType
// {
//         public static string CryptoCode = "TronUSDT";
//         public static TronUSDTPaymentType Instance { get; } = new();
//         public static PaymentMethodId PaymentMethodId { get; } = new(CryptoCode, TronUSDTPaymentType.Instance);
//
//         private TronUSDTPaymentType() { }
//
//         public override string ToPrettyString() => "On-Chain";
//         public override string GetId() => "TronUSDTLike";
//         public override string ToStringNormalized() => "Tron USDT";
//
//         public override CryptoPaymentData DeserializePaymentData(BTCPayNetworkBase network, string str)
//         {
//             return JsonConvert.DeserializeObject<TronUSDTPaymentData>(str);
//         }
//
//         public override string SerializePaymentData(BTCPayNetworkBase network, CryptoPaymentData paymentData)
//         {
//             return JsonConvert.SerializeObject(paymentData);
//         }
//         
//         public override string InvoiceViewPaymentPartialName { get; } = "Bitcoin/ViewBitcoinLikePaymentData";
//         public override bool IsPaymentType(string paymentType)
//         {
//             return string.IsNullOrEmpty(paymentType) || base.IsPaymentType(paymentType);
//         }
//     }
//
//
// public class TronUSDTPaymentLinkExtension : IPaymentLinkExtension
// {
//     public PaymentMethodId PaymentMethodId => TronUSDTPaymentType.PaymentMethodId;
//
//     public string GetPaymentLink(PaymentPrompt prompt, IUrlHelper urlHelper)
//     {
//         throw new System.NotImplementedException();
//     }
// }

// public class TronUSDTPaymentMethodViewExtension : IPaymentMethodViewExtension
// {
//     public PaymentMethodId PaymentMethodId => TronUSDTPaymentType.PaymentMethodId;
//     public void RegisterViews(PaymentMethodViewContext context)
//     {
//         throw new System.NotImplementedException();
//     }
// }

// public class TronUSDTPaymentModelExtension : IPaymentModelExtension
// {
//     public PaymentMethodId PaymentMethodId => TronUSDTPaymentType.Instance;
//     public string DisplayName  => "tt_displayname";
//     public string Image  => "tt_image";
//     public string Badge => "tt_badge";
//     public void ModifyPaymentModel(PaymentModelContext context)
//     {
//         throw new System.NotImplementedException();
//     }
// }

// public class TronUSDTPaymentMethodHandler : IPaymentMethodHandler
// {
//     public PaymentMethodId PaymentMethodId => 
//     public Task ConfigurePrompt(PaymentMethodContext context)
//     {
//         throw new System.NotImplementedException();
//     }
//
//     public Task BeforeFetchingRates(PaymentMethodContext context)
//     {
//         throw new System.NotImplementedException();
//     }
//
//     public JsonSerializer Serializer => throw new NotImplementedException();
//     public object ParsePaymentPromptDetails(JToken details)
//     {
//         throw new System.NotImplementedException();
//     }
//
//     public object ParsePaymentMethodConfig(JToken config)
//     {
//         throw new System.NotImplementedException();
//     }
//
//     public object ParsePaymentDetails(JToken details)
//     {
//         throw new System.NotImplementedException();
//     }
// }
//
// public class TronUSDTPaymentData : CryptoPaymentData
// {
//     public string GetPaymentProof()
//     {
//         throw new System.NotImplementedException();
//     }
// }
//