using System;
using System.Linq;
using System.Text;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Plugins.USDt.Services;

public static class TronUSDtAddressHelper
{
    public static string HexToBase58(string hexString)
    {
        hexString = hexString.Replace("0x", "41");
        var data = HexToByteArray(hexString);
        return new Base58CheckEncoder().EncodeData(data);
    }

    public static string Base58ToHex(string base58String)
    {
        var decodedData = new Base58CheckEncoder().DecodeData(base58String);
        if (decodedData[0] != 0x41)
            throw new FormatException();

        var hexString = ByteArrayToHex(decodedData.Skip(1).ToArray());
        return "0x" + hexString;
    }

    public static bool IsValid(string tron)
    {
        try
        {
            var bytes = new Base58CheckEncoder().DecodeData(tron);

            return bytes[0] == 0x41;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string ByteArrayToHex(byte[] bytes)
    {
        var hex = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) hex.AppendFormat("{0:x2}", b);
        return hex.ToString();
    }

    private static byte[] HexToByteArray(string hexString)
    {
        var numberChars = hexString.Length;
        var bytes = new byte[numberChars / 2];
        for (var i = 0; i < numberChars; i += 2) bytes[i / 2] = Convert.ToByte(hexString.Substring(i, 2), 16);
        return bytes;
    }
}