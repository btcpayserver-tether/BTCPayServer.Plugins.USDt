using BTCPayServer.Plugins.TronUSDT.Services;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    [Trait("Fast", "Fast")]
    public class FastTests : UnitTestBase
    {
        public FastTests(ITestOutputHelper helper) : base(helper)
        {
        }
    
        
        [Fact]
        public void TronConversion()
        {
            Assert.Equal("TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs",TronUSDTAddressHelper.HexToBase58("0x42a1e39aefa49290f2b3f9ed688d7cecf86cd6e0"));
            Assert.Equal("0x42a1e39aefa49290f2b3f9ed688d7cecf86cd6e0",TronUSDTAddressHelper.Base58ToHex("TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs"));
            Assert.True(TronUSDTAddressHelper.IsValid("TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs"));
            Assert.False(TronUSDTAddressHelper.IsValid("TG2XXyExBkPp9nzdajDZsozEu4BkaSJozs"));
            Assert.False(TronUSDTAddressHelper.IsValid("TG3xXyExBkPp9nzdajDZsozEu4BkaSJozs"));
        }
    }
}
