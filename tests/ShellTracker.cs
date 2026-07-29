using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

/// <summary>
/// Keeps hold of every shell a fixture builds so tear-down can stop them.
/// </summary>
/// <remarks>
/// A shell starts a four-second device poll, and the app only ever has one, for as long as it runs.
/// A fixture builds one per case, so without this the timers pile up and keep rebuilding the sidebars
/// of shells the test has finished with. Headless there is no synchronization context to post that
/// rebuild to, so it runs on the timer's thread and mutates the sections a later test is reading —
/// which showed up as a favourite that had just been pinned going missing, and only on the slower
/// runners.
/// </remarks>
internal sealed class ShellTracker
{
    private readonly List<MainWindowViewModel> _shells = [];

    public MainWindowViewModel Track(MainWindowViewModel shell)
    {
        _shells.Add(shell);
        return shell;
    }

    public void DisposeAll()
    {
        foreach (var shell in _shells)
            shell.Dispose();

        _shells.Clear();
    }
}
