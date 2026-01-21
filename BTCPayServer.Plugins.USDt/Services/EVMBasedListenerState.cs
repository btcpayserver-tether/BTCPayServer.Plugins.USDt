using System.Numerics;

namespace BTCPayServer.Plugins.USDt.Services;

public class EVMBasedListenerState
{
    public BigInteger LastBlockHeight { get; set; }
}