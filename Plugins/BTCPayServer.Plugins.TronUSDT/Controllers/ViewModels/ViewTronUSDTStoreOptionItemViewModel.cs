namespace BTCPayServer.Plugins.TronUSDT.Controllers.ViewModels;

public class ViewTronUSDTStoreOptionItemViewModel
{
    public required string DisplayName { get; init; }
    public bool Enabled { get; init; }
    public required string CryptoCode { get; init; }
    public required string[] Addresses { get; init; }
}