using FoileBrowser.Services;
using FoileBrowser.ViewModels;
using FoileBrowser.Views;

namespace FoileBrowser;

/// <summary>
/// Composition root: builds the services, hands them to the shell view-model and returns the window
/// the message loop runs. Deliberately the only place that knows every concrete service type.
/// </summary>
public static class App
{
    public static MainForm CreateShell()
    {
        var fileSystem = new FileSystemService();
        var settings = new SettingsService();
        // The copy engine reads its buffer/strategy tunables live from settings each transfer.
        var operations = new FileOperationService(() => settings.Current.ToCopyOptions());
        var trash = new TrashService();
        var search = new SearchService();
        var preview = new PreviewService();
        var tags = new TagService(settings);
        var shell = new ShellService();
        var archives = new ArchiveService();

        var viewModel = new MainWindowViewModel(
            fileSystem, operations, trash, search, preview, settings, tags, shell, archives);

        return new MainForm(viewModel);
    }
}
