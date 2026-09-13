using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.ModelBinders;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.USDt.Configuration;
using BTCPayServer.Plugins.USDt.Controllers;
using BTCPayServer.Plugins.USDt.Services;
using BTCPayServer.Plugins.USDt.Services.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Ganss.Xss;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBXplorer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Xunit;
using AuthenticationSchemes = BTCPayServer.Abstractions.Constants.AuthenticationSchemes;

namespace BTCPayServer.Plugins.USDt.Tests;

// Exercise the real Razor forms, MVC binding/validation, controllers and store
// repository. Only authentication, invoice/RPC data and the database provider are
// test fixtures; no blockchain, browser install or PostgreSQL service is needed.
[Trait("Fast", "Fast")]
public class StorePaymentMethodFormTests
{
    [Theory]
    [InlineData("ETHEREUM")]
    [InlineData("POLYGON")]
    [InlineData("BSC")]
    [InlineData("TRON")]
    public async Task SaveUnchangedDisableAndReenableFromRenderedForm(string chain)
    {
        using var host = await FormHost.Create();
        foreach (var enabled in new[] { true, false, true })
        {
            var page = await host.GetPage(chain);
            var form = SaveForm(page);
            Assert.Null(form.QuerySelector("[name=DisplayName]"));
            Assert.Null(form.QuerySelector("[name=ChainDisplayName]"));
            ((IHtmlInputElement)form.QuerySelector("#Enabled")!).IsChecked = enabled;

            using var response = await host.Submit(form);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var saved = await host.ReadStore();
            Assert.Equal(!enabled, saved.GetStoreBlob().IsExcluded(FormHost.Id(chain)));
            Assert.All(host.Handlers.Select(handler => handler.PaymentMethodId).Where(id => id != FormHost.Id(chain)),
                id => Assert.False(saved.GetStoreBlob().IsExcluded(id)));
            Assert.Equal(new[] { FormHost.Address(chain) }, host.Config(saved, chain).Addresses);
            var reloaded = await host.GetPage(chain);
            Assert.Equal(enabled, ((IHtmlInputElement)reloaded.QuerySelector("#Enabled")!).IsChecked);
            Assert.Null(reloaded.QuerySelector(".validation-summary-errors"));
            var alert = reloaded.QuerySelector(".alert-danger");
            Assert.True(alert is null, alert?.TextContent);
        }
    }

    [Theory]
    [InlineData("ETHEREUM", "0", null)]
    [InlineData("ETHEREUM", "2", null)]
    [InlineData("ETHEREUM", "3", "ethereum:{smartContractAddress}@{chainId}/transfer?address={to}&uint256={amountUnits}")]
    [InlineData("TRON", "0", null)]
    [InlineData("TRON", "1", null)]
    [InlineData("TRON", "2", null)]
    [InlineData("TRON", "3", "tron:{to}?amount={amount}")]
    public async Task SupportedFormatsRoundTrip(string chain, string format, string? template)
    {
        using var host = await FormHost.Create();
        var form = SaveForm(await host.GetPage(chain));
        ((IHtmlSelectElement)form.QuerySelector("#PaymentLinkFormat")!).Value = format;
        ((IHtmlTextAreaElement)form.QuerySelector("#PaymentLinkTemplate")!).Value = template ?? "";
        using var response = await host.Submit(form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var saved = await host.ReadStore();
        Assert.Equal(int.Parse(format), (int)host.Format(saved, chain));
        Assert.Equal(template, host.Template(saved, chain));
        var reloaded = await host.GetPage(chain);
        Assert.Equal(format, ((IHtmlSelectElement)reloaded.QuerySelector("#PaymentLinkFormat")!).Value);
        Assert.Equal(template ?? "", ((IHtmlTextAreaElement)reloaded.QuerySelector("#PaymentLinkTemplate")!).Value);
    }

    [Theory]
    [InlineData("ETHEREUM", "PaymentLinkFormat", "bogus")]
    [InlineData("TRON", "PaymentLinkFormat", "bogus")]
    [InlineData("ETHEREUM", "PaymentLinkFormat", "999")]
    [InlineData("TRON", "PaymentLinkFormat", "999")]
    [InlineData("ETHEREUM", "PaymentLinkFormat", "1")]
    [InlineData("ETHEREUM", "Enabled", "bogus")]
    [InlineData("TRON", "Enabled", "bogus")]
    public async Task InvalidInputShowsFieldErrorWithoutChangingSavedSettings(string chain, string field, string value)
    {
        using var host = await FormHost.Create();
        var before = await host.ReadStore();
        using var response = await host.Submit(SaveForm(await host.GetPage(chain)), new() { [field] = value });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await Parse(response);
        Assert.NotEmpty(page.QuerySelector($"[data-valmsg-for='{field}']")!.TextContent);
        Assert.DoesNotContain("The selected payment link format is invalid.", page.Body!.TextContent);
        var after = await host.ReadStore();
        Assert.Equal(before.StoreBlob, after.StoreBlob);
        Assert.Equal(before.DerivationStrategies, after.DerivationStrategies);
    }

    [Theory]
    [InlineData("ETHEREUM", "wallet:{unknown}")]
    [InlineData("TRON", "wallet:{unknown}")]
    [InlineData("ETHEREUM", "")]
    [InlineData("TRON", "")]
    public async Task InvalidTemplateIsPreservedAndCanBeCorrected(string chain, string invalid)
    {
        using var host = await FormHost.Create();
        var form = SaveForm(await host.GetPage(chain));
        ((IHtmlInputElement)form.QuerySelector("#Enabled")!).IsChecked = false;
        ((IHtmlSelectElement)form.QuerySelector("#PaymentLinkFormat")!).Value = "3";
        ((IHtmlTextAreaElement)form.QuerySelector("#PaymentLinkTemplate")!).Value = invalid;
        using var rejected = await host.Submit(form);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var page = await Parse(rejected);
        Assert.NotEmpty(page.QuerySelector("[data-valmsg-for='PaymentLinkTemplate']")!.TextContent);
        Assert.Equal(invalid, ((IHtmlTextAreaElement)page.QuerySelector("#PaymentLinkTemplate")!).Value);
        Assert.Equal("3", ((IHtmlSelectElement)page.QuerySelector("#PaymentLinkFormat")!).Value);
        Assert.False(((IHtmlInputElement)page.QuerySelector("#Enabled")!).IsChecked);
        Assert.False((await host.ReadStore()).GetStoreBlob().IsExcluded(FormHost.Id(chain)));

        form = SaveForm(page);
        ((IHtmlTextAreaElement)form.QuerySelector("#PaymentLinkTemplate")!).Value = "wallet:{to}";
        using var accepted = await host.Submit(form);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.True((await host.ReadStore()).GetStoreBlob().IsExcluded(FormHost.Id(chain)));
        Assert.Equal("wallet:{to}", host.Template(await host.ReadStore(), chain));
    }

    [Theory]
    [InlineData("ETHEREUM")]
    [InlineData("TRON")]
    public async Task EmptyAddAddressDoesNotSaveOrDisablePaymentMethod(string chain)
    {
        using var host = await FormHost.Create();
        var before = await host.ReadStore();
        var page = await host.GetPage(chain);
        var form = page.QuerySelector("#Address")!.Closest("form")!;
        using var response = await host.Submit(form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var after = await host.ReadStore();
        Assert.Equal(before.StoreBlob, after.StoreBlob);
        Assert.Equal(before.DerivationStrategies, after.DerivationStrategies);
        Assert.Contains("No addresses were added", (await host.GetPage(chain)).Body!.TextContent);
    }

    [Theory]
    [InlineData("ETHEREUM", "0x1111111111111111111111111111111111111111")]
    [InlineData("TRON", "TNPeeaaFB7K9cmo4uQpcU32zGK8G1NYqeL")]
    public async Task AddingAddressPreservesDisabledStateAndCustomFormat(string chain, string address)
    {
        using var host = await FormHost.Create();
        var form = SaveForm(await host.GetPage(chain));
        ((IHtmlInputElement)form.QuerySelector("#Enabled")!).IsChecked = false;
        ((IHtmlSelectElement)form.QuerySelector("#PaymentLinkFormat")!).Value = "3";
        ((IHtmlTextAreaElement)form.QuerySelector("#PaymentLinkTemplate")!).Value = "wallet:{to}";
        using var saved = await host.Submit(form);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        form = (await host.GetPage(chain)).QuerySelector("#Address")!.Closest("form")!;
        ((IHtmlInputElement)form.QuerySelector("#Address")!).Value = address;
        using var added = await host.Submit(form);
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        var store = await host.ReadStore();
        Assert.True(store.GetStoreBlob().IsExcluded(FormHost.Id(chain)));
        Assert.Equal(USDtPaymentLinkFormat.Custom, host.Format(store, chain));
        Assert.Equal("wallet:{to}", host.Template(store, chain));
        Assert.Equal(new[] { FormHost.Address(chain), address }, host.Config(store, chain).Addresses);
    }

    [Theory]
    [InlineData("ETHEREUM")]
    [InlineData("TRON")]
    public async Task DisplayFieldsCannotChangeSettingsAndAntiforgeryIsRequired(string chain)
    {
        using var host = await FormHost.Create();
        var form = SaveForm(await host.GetPage(chain));
        using var accepted = await host.Submit(form, new()
        {
            ["DisplayName"] = "",
            ["ChainDisplayName"] = "",
            ["Addresses[0].Value"] = "forged",
            ["TemplatePreviewChainId"] = "bogus"
        });
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.Equal(new[] { FormHost.Address(chain) }, host.Config(await host.ReadStore(), chain).Addresses);
        form = SaveForm(await host.GetPage(chain));
        form.QuerySelector("[name=__RequestVerificationToken]")!.Remove();
        ((IHtmlInputElement)form.QuerySelector("#Enabled")!).IsChecked = false;
        using var rejected = await host.Submit(form);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.False((await host.ReadStore()).GetStoreBlob().IsExcluded(FormHost.Id(chain)));
    }

    private static IElement SaveForm(IHtmlDocument page) => page.QuerySelector("#SaveButton")!.Closest("form")!;
    private static async Task<IHtmlDocument> Parse(HttpResponseMessage response) =>
        await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());

    private sealed class FormHost : IDisposable
    {
        private const string StoreId = "form-test";
        private readonly TestServer _server;
        private readonly IHost _host;
        private readonly HttpClient _client;
        private readonly TestDbFactory _database = new();
        public PaymentMethodHandlerDictionary Handlers { get; }
        public static PaymentMethodId Id(string chain) => new($"USDT-{chain}");
        public static string Address(string chain) => chain == "TRON"
            ? "TG3XXyExBkPp9nzdajDZsozEu4BkaSJozs" : "0x742d35cc6634c0532925a3b844bc454e4438f44e";

        public static async Task<FormHost> Create()
        {
            var host = new FormHost();
            await host._host.Services.GetRequiredService<CurrencyNameTable>().ReloadCurrencyData(CancellationToken.None);
            return host;
        }

        private FormHost()
        {
            var network = new NBXplorerNetworkProvider(ChainName.Mainnet);
            var settings = new ConfigurationBuilder().Build();
            var configuration = new USDtPluginConfiguration
            {
                EVMUSDtLikeConfigurationItems = USDtConfigurationProvider.GetEVMUSDtLikeDefaultConfigurationItems(network, settings)
                    .ToDictionary(pair => pair.Key, pair => pair.Value with { JsonRpcUri = new Uri("https://rpc.invalid") })
            };
            var tron = USDtConfigurationProvider.GetTronUSDtLikeDefaultConfigurationItem(network, settings)
                with { JsonRpcUri = new Uri("https://rpc.invalid") };
            configuration.TronUSDtLikeConfigurationItems.Add(tron.GetPaymentMethodId(), tron);
            Handlers = new PaymentMethodHandlerDictionary(configuration.EVMUSDtLikeConfigurationItems.Values
                .Select(c => (IPaymentMethodHandler)new EVMUSDtPaymentMethodHandler(c, null!, null!, null!))
                .Append(new TronUSDtLikePaymentMethodHandler(tron, null!, null!, null!)));
            using (var db = _database.CreateContext())
            {
                var store = new StoreData { Id = StoreId, StoreName = "Form test" };
                foreach (var handler in Handlers)
                {
                    USDtPaymentMethodConfig config = handler.PaymentMethodId == Id("TRON")
                        ? new TronUSDtPaymentMethodConfig { PaymentLinkFormat = USDtPaymentLinkFormat.Standard }
                        : new EVMUSDtPaymentMethodConfig { PaymentLinkFormat = USDtPaymentLinkFormat.Standard };
                    config.Addresses = [Address(handler.PaymentMethodId == Id("TRON") ? "TRON" : "ETHEREUM")];
                    config.MarkActivated();
                    store.SetPaymentMethodConfig(handler, config);
                }
                store.SetStoreBlob(store.GetStoreBlob());
                db.Stores.Add(store);
                db.SaveChanges();
            }

            _host = new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
            {
                services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Error));
                services.AddMemoryCache();
                services.AddHttpContextAccessor();
                services.AddSingleton<ApplicationDbContextFactory>(_database);
                services.AddSingleton(configuration);
                services.AddSingleton(Handlers);
                services.AddSingleton(new BTCPayServer.Logging.Logs());
                services.AddSingleton<EventAggregator>();
                services.AddSingleton(new JsonSerializerSettings());
                services.AddSingleton<SettingsRepository>();
                services.AddSingleton<StoreRepository>();
                services.AddSingleton(new USDtTrackedInvoiceProvider(new EmptyInvoices(), TimeProvider.System));
                services.AddSingleton<IHttpClientFactory, RpcFactory>();
                services.AddSingleton<EVMUSDtRPCProvider>();
                services.AddSingleton<TronUSDtRPCProvider>();
                services.AddSingleton<CurrencyDataProvider>(new InMemoryCurrencyDataProvider(
                    [new CurrencyData { Code = "USD", Name = "US Dollar", Divisibility = 2, Symbol = "$" }]));
                services.AddSingleton<CurrencyNameTable>();
                services.AddSingleton<DisplayFormatter>();
                services.AddScoped<Safe>();
                services.AddScoped<BTCPayServer.Security.ContentSecurityPolicies>();
                services.AddSingleton<HtmlSanitizer>();
                services.AddLocalization();
                services.AddTransient<Microsoft.AspNetCore.Mvc.Localization.ViewLocalizer>();
                services.AddTransient<IStringLocalizer, StringLocalizer<StorePaymentMethodFormTests>>();
                services.AddAuthentication(AuthenticationSchemes.Cookie)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthentication>(AuthenticationSchemes.Cookie, _ => { });
                services.AddAuthorization(options => options.AddPolicy(Policies.CanModifyStoreSettings,
                    policy => policy.RequireAuthenticatedUser()));
                services.AddControllersWithViews(options =>
                    {
                        options.ModelBinderProviders.Insert(0, new DefaultModelBinderProvider());
                        options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
                        options.Filters.Add(new RenderWithoutLayout());
                    })
                    .AddApplicationPart(typeof(UIEVMUSDtLikeStoreController).Assembly)
                    .AddApplicationPart(typeof(BTCPayServer.Components.Icon.Icon).Assembly)
                    .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new OnlyStoreControllers()))
                    .AddRazorOptions(options => options.ViewLocationFormats.Add("/{0}.cshtml"))
                    .AddViewLocalization();
            }).Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.Use(async (context, next) =>
                {
                    context.SetStoreData(await context.RequestServices.GetRequiredService<StoreRepository>().FindStore(StoreId));
                    await next();
                });
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            })).Start();
            _server = _host.GetTestServer();
            _client = new HttpClient(new Cookies { InnerHandler = _server.CreateHandler() })
                { BaseAddress = new Uri("https://localhost") };
        }

        public async Task<StoreData> ReadStore()
        {
            using var db = _database.CreateContext();
            return await db.Stores.SingleAsync(store => store.Id == StoreId);
        }

        public USDtPaymentMethodConfig Config(StoreData store, string chain) => chain == "TRON"
            ? store.GetPaymentMethodConfig<TronUSDtPaymentMethodConfig>(Id(chain), Handlers)!
            : store.GetPaymentMethodConfig<EVMUSDtPaymentMethodConfig>(Id(chain), Handlers)!;
        public USDtPaymentLinkFormat Format(StoreData store, string chain) => Config(store, chain) switch
        {
            TronUSDtPaymentMethodConfig c => c.PaymentLinkFormat!.Value,
            EVMUSDtPaymentMethodConfig c => c.PaymentLinkFormat!.Value,
            _ => throw new InvalidOperationException()
        };
        public string? Template(StoreData store, string chain) => Config(store, chain) switch
        {
            TronUSDtPaymentMethodConfig c => c.PaymentLinkTemplate,
            EVMUSDtPaymentMethodConfig c => c.PaymentLinkTemplate,
            _ => throw new InvalidOperationException()
        };

        public async Task<IHtmlDocument> GetPage(string chain)
        {
            using var response = await _client.GetAsync($"/stores/{StoreId}/{(chain == "TRON" ? "tron" : "evm")}USDtlike/{Id(chain)}");
            response.EnsureSuccessStatusCode();
            return await Parse(response);
        }

        public async Task<HttpResponseMessage> Submit(IElement form, Dictionary<string, string>? overrides = null)
        {
            var fields = new List<KeyValuePair<string, string>>();
            foreach (var element in form.QuerySelectorAll("input[name], select[name], textarea[name]"))
            {
                if (element.HasAttribute("disabled")) continue;
                if (element is IHtmlInputElement { Type: "checkbox" or "radio", IsChecked: false }) continue;
                var value = element switch
                {
                    IHtmlInputElement input => input.Value,
                    IHtmlSelectElement select => select.Value,
                    IHtmlTextAreaElement textarea => textarea.Value,
                    _ => throw new InvalidOperationException()
                };
                fields.Add(new(element.GetAttribute("name")!, value ?? ""));
            }
            foreach (var pair in overrides ?? [])
            {
                fields.RemoveAll(field => field.Key == pair.Key);
                fields.Add(pair);
            }
            using var content = new MultipartFormDataContent();
            foreach (var field in fields) content.Add(new StringContent(field.Value), field.Key);
            return await _client.PostAsync(form.GetAttribute("action"), content);
        }

        public void Dispose() { _client.Dispose(); _host.Dispose(); }
    }

    private sealed class TestDbFactory() : ApplicationDbContextFactory(
        Options.Create(new DatabaseOptions()), NullLoggerFactory.Instance)
    {
        private readonly DbContextOptions<ApplicationDbContext> _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public override ApplicationDbContext CreateContext(Action<NpgsqlDbContextOptionsBuilder>? npgsqlOptionsAction = null) => new(_options);
    }

    private sealed class EmptyInvoices : IUSDtInvoiceSource
    {
        public Task<InvoiceEntity[]> GetMonitoredInvoices(PaymentMethodId id, CancellationToken ct) => Task.FromResult<InvoiceEntity[]>([]);
        public Task<InvoiceEntity[]> GetExpiredInvoicesSince(DateTimeOffset since, CancellationToken ct) => Task.FromResult<InvoiceEntity[]>([]);
        public Task<InvoiceEntity[]> GetInvoices(string[] ids, CancellationToken ct) => Task.FromResult<InvoiceEntity[]>([]);
    }

    private sealed class RpcFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new RpcResponse());
        private sealed class RpcResponse : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var rpc = JObject.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal("eth_call", (string?)rpc["method"]);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(new JObject
                        { ["jsonrpc"] = "2.0", ["id"] = rpc["id"], ["result"] = "0x" + new string('0', 64) }.ToString())
                };
            }
        }
    }

    private sealed class OnlyStoreControllers : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in feature.Controllers.ToArray())
                if (controller.AsType() != typeof(UIEVMUSDtLikeStoreController) && controller.AsType() != typeof(UITronUSDtLikeStoreController))
                    feature.Controllers.Remove(controller);
        }
    }

    // Render the production settings view and partials without BTCPay's unrelated
    // site layout. Input/select/validation/antiforgery tag helpers remain real.
    private sealed class RenderWithoutLayout : IResultFilter
    {
        public void OnResultExecuting(ResultExecutingContext context)
        {
            if (context.Result is ViewResult view)
                context.Result = new PartialViewResult { ViewName = view.ViewName, ViewData = view.ViewData, TempData = view.TempData };
        }
        public void OnResultExecuted(ResultExecutedContext context) { }
    }

    private sealed class Cookies : DelegatingHandler
    {
        private readonly CookieContainer _cookies = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            request.Headers.TryAddWithoutValidation("Cookie", _cookies.GetCookieHeader(request.RequestUri!));
            var response = await base.SendAsync(request, ct);
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                foreach (var cookie in cookies) _cookies.SetCookies(request.RequestUri!, cookie);
            return response;
        }
    }

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "store-owner")],
                Scheme.Name)), Scheme.Name)));
    }
}
