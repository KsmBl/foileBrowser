using Avalonia;

namespace FoileBrowser;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't
    // initialized yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Default to CPU (software) rendering so the GPU/Mesa stack (libLLVM + libgallium, ~120 MB) is
    // never mapped into the process — the single biggest memory saving. Set FOILE_GPU=1 to keep the
    // smoother, lower-CPU GL renderer at the cost of that footprint.
    private static bool UseGpu => Environment.GetEnvironmentVariable("FOILE_GPU") == "1";

    // Avalonia configuration, don't remove; also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

        if (!UseGpu)
            builder = builder
                .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] })
                .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
                .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Software] });

        return builder;
    }
}
