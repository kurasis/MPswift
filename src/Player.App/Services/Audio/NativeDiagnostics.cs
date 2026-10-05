namespace Player.App.Services.Audio;

public interface INativeDiagnostics
{
    Task<IReadOnlyDictionary<string, string>> VerifyAsync();
}

public sealed class NativeDiagnostics : INativeDiagnostics
{
    public Task<IReadOnlyDictionary<string, string>> VerifyAsync() =>
        Task.Run(NativeLibraryBootstrap.LoadAndVerify);
}
