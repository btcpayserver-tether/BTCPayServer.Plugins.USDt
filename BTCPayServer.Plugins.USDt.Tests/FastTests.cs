using BTCPayServer.Data;
using BTCPayServer.Plugins.USDt.Services;
using BTCPayServer.Plugins.USDt.Services.Payments;
using BTCPayServer.Tests;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.USDt.Tests;

[Trait("Fast", "Fast")]
public class FastTests : UnitTestBase
{
    public FastTests(ITestOutputHelper helper) : base(helper)
    {
    }

    
    [Fact]
    public void TronConversion()
    {
        Assert.Equal("TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs",TronUSDtAddressHelper.HexToBase58("0x42a1e39aefa49290f2b3f9ed688d7cecf86cd6e0"));
        Assert.Equal("0x42a1e39aefa49290f2b3f9ed688d7cecf86cd6e0",TronUSDtAddressHelper.Base58ToHex("TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs"));
        Assert.True(TronUSDtAddressHelper.IsValid("TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs"));
        Assert.False(TronUSDtAddressHelper.IsValid("TG2XXyExBkPp9nzdajDZsozEu4BkaSJozs"));
        Assert.False(TronUSDtAddressHelper.IsValid("TG3xXyExBkPp9nzdajDZsozEu4BkaSJozs"));
    }

    [Fact]
    public void ExcludeAmountFromPaymentLinkSurvivesBlobSerializerRoundTrip()
    {
        var details = new TronUSDtLikeOnChainPaymentMethodDetails
        {
            ExcludeAmountFromPaymentLink = true
        };
        var serializer = BlobSerializer.CreateSerializer().Serializer;
        var json = JObject.FromObject(details, serializer);

        // BlobSerializer uses camelCase – the lookup key must match
        var value = json.Value<bool?>("excludeAmountFromPaymentLink");
        Assert.True(value, "The camelCase key must be used to read ExcludeAmountFromPaymentLink from prompt details");
    }
}
