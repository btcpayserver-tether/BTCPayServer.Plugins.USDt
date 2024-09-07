using System.Numerics;

namespace BTCPayServer.Plugins.TronUSDT.Services;

public class TronUSDTListenerState
{
    public BigInteger LastBlockHeight { get; set; }
}