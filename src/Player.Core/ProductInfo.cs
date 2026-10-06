namespace Player.Core;

public static class ProductInfo
{
    public const string Name = "MPswift";
    public static string Version { get; } = typeof(ProductInfo).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion.Split('+')[0];
}
