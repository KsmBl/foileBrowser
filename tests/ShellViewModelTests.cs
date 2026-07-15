using FoileBrowser.Models;
using FoileBrowser.Services;
using FoileBrowser.ViewModels;
using static FoileBrowser.Tests.FakeEntries;

namespace FoileBrowser.Tests;

[TestFixture]
public class ShellViewModelTests
{
    private string _settingsDir = null!;
    private string _settingsFile = null!;

    [SetUp]
    public void SetUp()
    {
        _settingsDir = Path.Combine(Path.GetTempPath(), "foile-shell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_settingsDir);
        _settingsFile = Path.Combine(_settingsDir, "settings.json");
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_settingsDir, recursive: true); } catch (IOException) { }
    }

    // Isolates settings/tags to a temp file so tests never touch the real user config.
    private MainWindowViewModel CreateShell(FakeFileSystem fs, RecordingTrash trash)
    {
        var settings = new SettingsService(_settingsFile);
        return new MainWindowViewModel(fs, new FileOperationService(), trash,
            new SearchService(), new PreviewService(), settings, new TagService(settings), new ShellService());
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(15);
        }
        return condition();
    }

    [Test]
    public async Task Initialize_Builds_Both_Panes_And_Sidebar()
    {
        var fs = new FakeFileSystem();
        fs.Entries.Add(Dir("child"));
        fs.Volumes.Add(new DriveVolume { Label = "System", RootPath = "/", FreeBytes = 50, TotalBytes = 100 });
        var vm = CreateShell(fs, new RecordingTrash());

        await vm.InitializeAsync();

        Assert.That(vm.LeftPane.ActiveTab, Is.Not.Null);
        Assert.That(vm.RightPane.ActiveTab, Is.Not.Null);
        Assert.That(vm.Sidebar.Any(s => s.Kind == SidebarItemKind.Header && s.Name == "Drives"), Is.True);
        var drive = vm.Sidebar.Single(s => s.Kind == SidebarItemKind.Drive);
        Assert.That(drive.Name, Is.EqualTo("System"));
        Assert.That(drive.UsedFraction, Is.EqualTo(0.5).Within(0.001));
    }

    [Test]
    public void ToggleDualPane_Flips_State_And_Forces_Left_Active()
    {
        var vm = CreateShell(new FakeFileSystem(), new RecordingTrash());
        Assert.That(vm.IsDualPane, Is.True);

        vm.ToggleDualPaneCommand.Execute(null);

        Assert.That(vm.IsDualPane, Is.False);
        Assert.That(vm.ActivePane, Is.SameAs(vm.LeftPane));
    }

    [Test]
    public async Task DeleteSelected_Sends_Selection_To_Trash()
    {
        var fs = new FakeFileSystem();
        fs.Entries.Add(File("gone.txt"));
        var trash = new RecordingTrash();
        var vm = CreateShell(fs, trash);
        await vm.InitializeAsync();

        vm.ActiveTab!.SelectedEntry = vm.ActiveTab.Entries.First();
        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.That(trash.Trashed, Has.Count.EqualTo(1));
        Assert.That(trash.Trashed[0], Does.EndWith("gone.txt"));
    }

    [Test]
    public async Task CopyToOther_Enqueues_When_Dual_Pane()
    {
        var fs = new FakeFileSystem();
        fs.Entries.Add(File("doc.txt"));
        var vm = CreateShell(fs, new RecordingTrash());
        await vm.InitializeAsync();

        vm.ActiveTab!.SelectedEntry = vm.ActiveTab.Entries.First();
        vm.CopyToOtherCommand.Execute(null);

        Assert.That(vm.OperationQueue.Operations, Has.Count.EqualTo(1));
        Assert.That(vm.OperationQueue.Operations[0].Kind, Is.EqualTo(FileOperationKind.Copy));
    }

    [Test]
    public void CopyToOther_Is_Disabled_In_Single_Pane()
    {
        var vm = CreateShell(new FakeFileSystem(), new RecordingTrash());
        vm.ToggleDualPaneCommand.Execute(null); // now single pane

        Assert.That(vm.CopyToOtherCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task OpenSidebarItem_Navigates_Active_Tab()
    {
        var fs = new FakeFileSystem();
        var vm = CreateShell(fs, new RecordingTrash());
        await vm.InitializeAsync();

        await vm.OpenSidebarItemCommand.ExecuteAsync(
            new SidebarItemViewModel { Name = "Root", Path = "/somewhere", Kind = SidebarItemKind.Favorite });

        Assert.That(vm.ActiveTab!.CurrentPath, Is.EqualTo("/somewhere"));
    }

    [Test]
    public async Task Inspector_Follows_Active_Tab_Selection()
    {
        var fs = new FakeFileSystem();
        fs.Entries.Add(File("doc.txt"));
        var preview = new FakePreview();
        var settings = new SettingsService(_settingsFile);
        var vm = new MainWindowViewModel(fs, new FileOperationService(), new RecordingTrash(),
            new SearchService(), preview, settings, new TagService(settings), new ShellService());
        await vm.InitializeAsync();

        vm.ActiveTab!.SelectedEntry = vm.ActiveTab.Entries.First();

        Assert.That(await WaitUntilAsync(() => vm.Preview is not null), Is.True);
        Assert.That(preview.Last!.Name, Is.EqualTo("doc.txt"));
    }

    [Test]
    public async Task ToggleInspector_Flips_Visibility()
    {
        var vm = CreateShell(new FakeFileSystem(), new RecordingTrash());
        await vm.InitializeAsync();
        Assert.That(vm.IsInspectorOpen, Is.True);

        vm.ToggleInspectorCommand.Execute(null);

        Assert.That(vm.IsInspectorOpen, Is.False);
    }

    [Test]
    public void CommandPalette_Has_Registered_Commands()
    {
        var vm = CreateShell(new FakeFileSystem(), new RecordingTrash());
        vm.OpenCommandPaletteCommand.Execute(null);

        Assert.That(vm.CommandPalette.IsOpen, Is.True);
        Assert.That(vm.CommandPalette.Results.Any(c => c.Title == "New Folder"), Is.True);
    }

    [Test]
    public async Task AssignTag_Tags_The_Selected_Entry()
    {
        var fs = new FakeFileSystem();
        fs.Entries.Add(File("tagged.txt"));
        var vm = CreateShell(fs, new RecordingTrash());
        await vm.InitializeAsync();
        vm.ActiveTab!.SelectedEntry = vm.ActiveTab.Entries.First();

        await vm.AssignTagCommand.ExecuteAsync("#E5484D");

        Assert.That(vm.ActiveTab.Entries.First(e => e.Name == "tagged.txt").TagColor, Is.EqualTo("#E5484D"));
    }

    [Test]
    public async Task Session_Is_Saved_And_Restored()
    {
        var fs = new FakeFileSystem();
        var vm1 = CreateShell(fs, new RecordingTrash());
        await vm1.InitializeAsync();
        await vm1.LeftPane.ActiveTab!.NavigateToAsync("/x/projects");
        await vm1.SaveSessionAsync();

        var vm2 = CreateShell(fs, new RecordingTrash());
        await vm2.InitializeAsync();

        Assert.That(vm2.LeftPane.ActiveTab!.CurrentPath, Is.EqualTo("/x/projects"));
    }

    [Test]
    public void CopyName_Requests_Clipboard_Copy()
    {
        var fs = new FakeFileSystem();
        fs.Entries.Add(File("clip.txt"));
        var vm = CreateShell(fs, new RecordingTrash());
        string? captured = null;
        vm.ClipboardCopyRequested += (_, text) => captured = text;

        // Populate a selection synchronously via a direct tab load.
        var tab = vm.LeftPane.AddTab();
        tab.NavigateToAsync("/x").GetAwaiter().GetResult();
        tab.SelectedEntry = tab.Entries.First();
        vm.CopyNameCommand.Execute(null);

        Assert.That(captured, Is.EqualTo("clip.txt"));
    }
}
