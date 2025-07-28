using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.USDt.Controllers;

public class SwaggerProvider : ISwaggerProvider
{
    public Task<JObject> Fetch()
    {
        var swaggerJson = @"
        {
          ""paths"": {
            ""/api/v1/stores/{storeId}/tronUSDtlike/{paymentMethodId}"": {
              ""get"": {
                ""tags"": [""Store (Payment Methods)""],
                ""summary"": ""Get Tron USDt configuration"",
                ""operationId"": ""GetUSDtLikeStoreInformation"",
                ""security"": [
                    {
                        ""API_Key"": [
                            ""btcpay.store.canviewstoresettings""
                        ],
                        ""Basic"": []
                    }
                ],
                ""parameters"": [
                  {
                      ""$ref"": ""#/components/parameters/StoreId""
                  },
                  {
                      ""$ref"": ""#/components/parameters/PaymentMethodId""
                  },
                ],
                ""responses"": {
                  ""200"": {
                    ""description"": ""Success"",
                    ""content"": {
                      ""application/json"": {
                        ""schema"": {
                          ""type"": ""object"",
                          ""properties"": {
                            ""storeId"": {""type"": ""string""},
                            ""paymentMethodId"": {""type"": ""string""},
                            ""enabled"": {""type"": ""boolean""},
                            ""addresses"": {
                              ""type"": ""array"",
                              ""items"": {
                                ""type"": ""object"",
                                ""properties"": {
                                  ""value"": {""type"": ""string""},
                                  ""available"": {""type"": ""boolean""},
                                  ""balance"": {
                                    ""type"": ""number"",
                                    ""nullable"": true
                                  }
                                }
                              }
                            }
                          }
                        }
                      }
                    }
                  },
                  ""404"": {
                    ""description"": ""Not Found""
                  }
                }
              }
            }
          }
        }";

        var jObject = JObject.Parse(swaggerJson);
        return Task.FromResult(jObject);
    }
}