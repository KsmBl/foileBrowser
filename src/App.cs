using FoileBrowser.Services;
using FoileBrowser.ViewModels;
using FoileBrowser.Views;

namespace FoileBrowser;

/// <summary>
/// Composition root: builds the services once and hands them to a view-model per window.
///
/// The services are shared across every window this process opens (PRD §6.12) — one settings file,
/// one tag store, one directory-size cache — so a second window costs a view-model and its controls
/// rather than a second copy of everything. Deliberately the only place that knows a concrete
/// service type.
/// </summary>
public static class App
{
    private static SettingsService? _settings;
    private static FileSystemService? _fileSystem;
    private static FileOperationService? _operations;
    private static TrashService? _trash;
    private static SearchService? _search;
    private static PreviewService? _preview;
    private static TagService? _tags;
    private static ShellService? _shell;
    private static ArchiveService? _archives;

    /// <summary>The window the message loop is started on; it is the one that persists the session.</summary>
    public static MainForm CreateShell() => new(NewViewModel(), primary: true);

    /// <summary>An additional window on the same services and the same process.</summary>
    public static MainForm CreateWindow() => new(NewViewModel(), primary: false);

    private static MainWindowViewModel NewViewModel()
    {
        _settings ??= new SettingsService();
        _fileSystem ??= new FileSystemService();
        // The copy engine reads its buffer/strategy tunables live from settings each transfer.
        _operations ??= new FileOperationService(() => _settings.Current.ToCopyOptions());
        _trash ??= new TrashService();
        _search ??= new SearchService();
        _preview ??= new PreviewService();
        _tags ??= new TagService(_settings);
        _shell ??= new ShellService();
        _archives ??= new ArchiveService();

        return new MainWindowViewModel(
            _fileSystem, _operations, _trash, _search, _preview, _settings, _tags, _shell, _archives);
    }
}
