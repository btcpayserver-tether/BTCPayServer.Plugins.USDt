namespace BTCPayServer.Plugins.USDt.Services;

/// <summary>
/// Tracking state for EVM-based blockchain listeners.
/// Stores the last indexed block height for resuming after restarts.
/// </summary>
public class EVMBasedListenerState
{
    public long LastBlockHeight { get; set; }
}