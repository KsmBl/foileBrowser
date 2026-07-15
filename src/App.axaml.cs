using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using FoileBrowser.Services;
using FoileBrowser.ViewModels;
using FoileBrowser.Views;

namespace FoileBrowser;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avalonia data validation and the CommunityToolkit both hook
            // INotifyDataErrorInfo; disable Avalonia's to avoid duplicate entries.
            DisableAvaloniaDataAnnotationValidation();

            var fileSystem = new FileSystemService();
            var operations = new FileOperationService();
            var trash = new TrashService();
            var search = new SearchService();
            var preview = new PreviewService();
            var settings = new SettingsService();
            var tags = new TagService(settings);
            var shell = new ShellService();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(
                    fileSystem, operations, trash, search, preview, settings, tags, shell),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
            BindingPlugins.DataValidators.Remove(plugin);
    }
}
