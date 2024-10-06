namespace BTCPayServer.Plugins.USDt.Configuration;

public record struct ChainRef
{
    private string Value { get; }

    private ChainRef(string value)
    {
        Value = value.ToUpperInvariant();
    }
    
    public static implicit operator ChainRef(string value)
    {
        return new ChainRef(value);
    }
    
    public override string ToString()
    {
        return Value;
    }
}