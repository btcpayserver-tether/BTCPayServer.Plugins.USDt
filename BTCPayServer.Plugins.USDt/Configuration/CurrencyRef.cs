namespace BTCPayServer.Plugins.USDt.Configuration;

public record struct CurrencyRef
{
    private string Value { get; }

    private CurrencyRef(string value)
    {
        Value = value.ToUpperInvariant();
    }
    public static implicit operator CurrencyRef(string value)
    {
        return new CurrencyRef(value);
    }

    public override string ToString()
    {
        return Value;
    }
}