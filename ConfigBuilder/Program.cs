using System.Reflection;
using System.Text.Json;

var solutionFolder = Directory.GetDirectories("../../../../Plugins/");
var plugin = solutionFolder.Single(e => Path.GetFileName(e) == "BTCPayServer.Plugins.TronUSDT");
var assemblyConfigurationAttribute = typeof(Program).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>();
var buildConfigurationName = assemblyConfigurationAttribute?.Configuration;
        
string[] path = [$"{Path.GetFullPath(plugin)}/bin/{buildConfigurationName}/net8.0/{Path.GetFileName(plugin)}.dll;"];

var content = JsonSerializer.Serialize(new
{
    DEBUG_PLUGINS = path
});

Console.WriteLine(content);
await File.WriteAllTextAsync("../../../../submodules/BTCPayServer/BTCPayServer/appsettings.dev.json", content);