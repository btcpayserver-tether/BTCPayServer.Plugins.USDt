using System;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Plugins.USDt.Services;

namespace BTCPayServer.Plugins.USDt.Configuration;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public class TronBase58Attribute : ValidationAttribute
{
    public TronBase58Attribute()
    {
        this.ErrorMessage = "{0} is not a Tron address (Base58 format expected)..";
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

        return TronUSDtAddressHelper.IsValid(valueAsString);
    }
}

public class TronUSDtLikeConfigurationItem
{
    public required Uri JsonRpcUri { get; init; }
    public required string SmartContractAddress { get; init; }
}