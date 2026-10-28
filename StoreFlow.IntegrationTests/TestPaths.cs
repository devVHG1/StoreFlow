namespace StoreFlow.IntegrationTests;

internal static class TestPaths
{
    public static string ApiDll(string root) => Path.Combine(root, "StoreFlow.Аpi", "bin",
        new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net8.0", "StoreFlow.Аpi.dll");
}
