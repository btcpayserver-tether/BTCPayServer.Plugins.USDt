using System;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Plugins.TronUSDT.Services;

namespace BTCPayServer.Plugins.TronUSDT.Configuration;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public class TronBase58Attribute : ValidationAttribute
{
    public TronBase58Attribute()
    {
        this.ErrorMessage = "{0} is not a TRON address (Base58 format expected)..";
    }

    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value is not string valueAsString)
        {
            return false;
        }

        return TronUSDTAddressHelper.IsValid(valueAsString);
    }
}

public class TronUSDTLikeConfigurationItem
{
    public required Uri JsonRpcUri { get; init; }
    public required string SmartContractAddress { get; init; }
}