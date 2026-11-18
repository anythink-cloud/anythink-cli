using System.Runtime.CompilerServices;

namespace AnythinkMcp.Tests;

internal static class TestServerConfig
{
    [ModuleInitializer]
    internal static void StopWatchingConfigFiles() =>
        Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");
}
