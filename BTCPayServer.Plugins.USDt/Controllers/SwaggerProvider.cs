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
            ""/api/v1/stores/{storeId}/evmUSDtlike/{paymentMethodId}"": {
              ""get"": {
                ""tags"": [""Store (Payment Methods)""],
                ""summary"": ""Get EVM USDt configuration"",
                ""operationId"": ""GetEvmUSDtLikeStoreInformation"",
                ""security"": [{""API_Key"": [""btcpay.store.canviewstoresettings""], ""Basic"": []}],
                ""parameters"": [
                  {""$ref"": ""#/components/parameters/StoreId""},
                  {""$ref"": ""#/components/parameters/PaymentMethodId""}
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
                                  ""balance"": {""type"": ""number"", ""nullable"": true}
                                }
                              }
                            }
                          }
                        }
                      }
                    }
                  },
                  ""404"": {""description"": ""Not Found""}
                }
              }
            },
            ""/api/v1/stores/{storeId}/evmUSDtlike/{paymentMethodId}/addresses"": {
              ""post"": {
                ""tags"": [""Store (Payment Methods)""],
                ""summary"": ""Add an EVM USDt address"",
                ""operationId"": ""AddEvmUSDtAddress"",
                ""security"": [{""API_Key"": [""btcpay.store.canmodifystoresettings""], ""Basic"": []}],
                ""parameters"": [
                  {""$ref"": ""#/components/parameters/StoreId""},
                  {""$ref"": ""#/components/parameters/PaymentMethodId""}
                ],
                ""requestBody"": {
                  ""required"": true,
                  ""content"": {
                    ""application/json"": {
                      ""schema"": {
                        ""type"": ""object"",
                        ""required"": [""addresses""],
                        ""properties"": {
                          ""addresses"": {
                            ""type"": ""array"",
                            ""items"": {""type"": ""string""},
                            ""description"": ""List of EVM addresses (0x...)""
                          }
                        }
                      }
                    }
                  }
                },
                ""responses"": {
                  ""200"": {""description"": ""Addresses added""},
                  ""400"": {""description"": ""Invalid address(es) or already exist""},
                  ""404"": {""description"": ""Payment method not found""}
                }
              }
            },
            ""/api/v1/stores/{storeId}/evmUSDtlike/{paymentMethodId}/addresses/{address}"": {
              ""delete"": {
                ""tags"": [""Store (Payment Methods)""],
                ""summary"": ""Remove an EVM USDt address"",
                ""operationId"": ""DeleteEvmUSDtAddress"",
                ""security"": [{""API_Key"": [""btcpay.store.canmodifystoresettings""], ""Basic"": []}],
                ""parameters"": [
                  {""$ref"": ""#/components/parameters/StoreId""},
                  {""$ref"": ""#/components/parameters/PaymentMethodId""},
                  {
                    ""name"": ""address"",
                    ""in"": ""path"",
                    ""required"": true,
                    ""schema"": {""type"": ""string""}
                  }
                ],
                ""responses"": {
                  ""200"": {""description"": ""Address removed""},
                  ""404"": {""description"": ""Address or payment method not found""}
                }
              }
            },
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