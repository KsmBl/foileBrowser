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
    public async Task Initialize_Opens_One_Pane_And_The_Sidebar()
    {
        var fs = new FakeFileSystem();
        fs.Entries.Add(Dir("child"));
        fs.Volumes.Add(new DriveVolume { Label = "System", RootPath = "/", FreeBytes = 50, TotalBytes = 100 });
        var vm = CreateShell(fs, new RecordingTrash());

        await vm.InitializeAsync();

        // A profile with no saved session opens a single pane; splitting is a command away.
        Assert.That(vm.Tabs, Has.Count.EqualTo(1), "one pane on a fresh profile");
        Assert.That(vm.ActiveTab, Is.Not.Null);
        Assert.That(vm.IsDualPane, Is.False);
        var drives = vm.Sections.Single(s => s.Id == "drives");
        Assert.That(drives.Title, Is.EqualTo("Drives"));
        var drive = drives.Items.Single(s => s.Kind == SidebarItemKind.Drive);
        Assert.That(drive.Name, Is.EqualTo("System"));
        Assert.That(drive.UsedFraction, Is.EqualTo(0.5).Within(0.001));
    }

    [Test]
    public async Task Sidebar_Groups_Partitions_Under_Their_Disk()
    {
        var fs = new FakeFileSystem();
        fs.Volumes.Add(new DriveVolume { Label = "root", RootPath = "/", Device = "/dev/sda2", Disk = "sda", FreeBytes = 1, TotalBytes = 2 });
        fs.Volumes.Add(new DriveVolume { Label = "boot", RootPath = "/boot", Device = "/dev/sda1", Disk = "sda", FreeBytes = 1, TotalBytes = 2 });
        var vm = CreateShell(fs, new RecordingTrash());

        await vm.InitializeAsync();

        var drives = vm.Sections.Single(s => s.Id == "drives").Items;
        Assert.That(drives.Any(s => s.Kind == SidebarItemKind.Disk && s.Name == "sda"), Is.True, "a disk group row");
        Assert.That(drives.Count(s => s.Kind == SidebarItemKind.Partition), Is.EqualTo(2), "both partitions listed under it");
        Assert.That(vm.Sections.Any(s => s.Id == "devices"), Is.False, "no partition shown as a device");
    }

    [Test]
    public async Task Sidebar_Shows_Single_Partition_Disk_As_One_Drive()
    {
        var fs = new FakeFileSystem();
        fs.Volumes.Add(new DriveVolume { Label = "root", RootPath = "/", Device = "/dev/nvme0n1p1", Disk = "nvme0n1" });
        var vm = CreateShell(fs, new RecordingTrash());

        await vm.InitializeAsync();

        var drives = vm.Sections.Single(s => s.Id == "drives").Items;
        Assert.That(drives.Any(s => s.Kind == SidebarItemKind.Disk), Is.False, "no group header for a single partition");
        Assert.That(drives.Count(s => s.Kind == SidebarItemKind.Drive), Is.EqualTo(1));
    }

    [Test]
    public async Task AddPane_Works_After_All_Tabs_Are_Closed()
    {
        var vm = CreateShell(new FakeFileSystem(), new RecordingTrash());
        await vm.InitializeAsync();

        // Close every tab the way the dock UI's close button does.
        foreach (var tab in vm.Tabs.ToList())
            vm.Layout.CloseTab(tab);
        Assert.That(vm.Tabs, Is.Empty);

        await vm.AddPaneCommand.ExecuteAsync(null);

        Assert.That(vm.Tabs, Has.Count.EqualTo(1));
        Assert.That(vm.ActiveTab, Is.Not.Null, "the new pane has a working tab");
    }

    [Test]
    public async Task AddTab_And_AddPane_Grow_The_Tab_Set()
    {
        var vm = CreateShell(new FakeFileSystem(), new RecordingTrash());
        await vm.InitializeAsync();
        var initial = vm.Tabs.Count;

        await vm.AddTabCommand.ExecuteAsync(null);
        await vm.AddPaneCommand.ExecuteAsync(null);

        Assert.That(vm.Tabs, Has.Count.EqualTo(initial + 2));
        Assert.That(vm.ActiveTab, Is.Not.Null);
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
        await vm.AddPaneCommand.ExecuteAsync(null); // a fresh profile starts single-pane
        vm.ActivateTab(vm.Tabs[0]);

        vm.ActiveTab!.SelectedEntry = vm.ActiveTab.Entries.First();
        vm.CopyToOtherCommand.Execute(null);

        Assert.That(vm.OperationQueue.Operations, Has.Count.EqualTo(1));
        Assert.That(vm.OperationQueue.Operations[0].Kind, Is.EqualTo(FileOperationKind.Copy));
    }

    [Test]
    public async Task CopyToOther_Is_Disabled_With_A_Single_Pane()
    {
        var vm = CreateShell(new FakeFileSystem(), new RecordingTrash());
        await vm.InitializeAsync();

        // Close the second pane's tab, leaving a single pane (no "other" to copy to).
        vm.Layout.CloseTab(vm.Tabs.Last());

        Assert.That(vm.IsDualPane, Is.False);
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
        await vm1.Tabs[0].NavigateToAsync("/x/projects");
        await vm1.SaveSessionAsync();

        var vm2 = CreateShell(fs, new RecordingTrash());
        await vm2.InitializeAsync();

        Assert.That(vm2.Tabs[0].CurrentPath, Is.EqualTo("/x/projects"));
    }

    [Test]
    public void CopyName_Requests_Clipboard_Copy()
    {
        var fs = new FakeFileSystem();
        fs.Entries.Add(File("clip.txt"));
        var vm = CreateShell(fs, new RecordingTrash());
        string? captured = null;
        vm.ClipboardCopyRequested += (_, text) => captured = text;

        // Populate a selection synchronously via a direct tab load on the active tab.
        var tab = vm.ActiveTab!;
        tab.NavigateToAsync("/x").GetAwaiter().GetResult();
        tab.SelectedEntry = tab.Entries.First();
        vm.CopyNameCommand.Execute(null);

        Assert.That(captured, Is.EqualTo("clip.txt"));
    }

    // ---- toolbar (PRD §6.3/§6.8) ----

    [Test]
    public async Task Every_Toolbar_Button_Says_What_It_Does()
    {
        var vm = CreateShell(new FakeFileSystem(), new RecordingTrash());
        await vm.InitializeAsync();

        Assert.That(vm.ToolbarItems, Is.Not.Empty);

        // Most of the bar is icon-only, and a drawn icon states nothing about itself. The hover text
        // is the only place a button explains what it is, so a blank one is a button nobody can read.
        var mute = vm.ToolbarItems.Where(i => string.IsNullOrWhiteSpace(i.Tooltip)).Select(i => i.Id);
        Assert.That(mute, Is.Empty, "every toolbar button needs hover text");
    }

    // ---- search row visibility (PRD §6.4): off means "only show it on Ctrl+F" ----

    [Test]
    public async Task The_Subtree_Search_Row_Stays_Out_Of_The_Way_Until_It_Is_Asked_For()
    {
        var vm = CreateShell(new FakeFileSystem(), new RecordingTrash());
        await vm.InitializeAsync();

        Assert.That(vm.IsSearchBarVisible, Is.False,
            "searching a subtree is occasional, so the row it needs is not part of the default chrome");
    }

    [Test]
    public async Task Search_Bars_Stay_Hidden_At_Startup_When_The_Setting_Is_Off()
    {
        System.IO.File.WriteAllText(_settingsFile, """{ "SearchBarVisible": false }""");
        var vm = CreateShell(new FakeFileSystem(), new RecordingTrash());
        await vm.InitializeAsync();

        Assert.That(vm.IsSearchBarVisible, Is.False,
            "with the setting off the bars are hidden until the user asks for them");
    }

    [Test]
    public async Task Ctrl_F_Reveals_Hidden_Search_Bars_And_Escape_Hides_Them_Again()
    {
        System.IO.File.WriteAllText(_settingsFile, """{ "SearchBarVisible": false }""");
        var vm = CreateShell(new FakeFileSystem(), new RecordingTrash());
        await vm.InitializeAsync();

        vm.FocusSearchCommand.Execute(null);   // Ctrl+F
        Assert.That(vm.IsSearchBarVisible, Is.True, "Ctrl+F reveals the bars on demand");

        vm.CollapseSearchBar();                 // Escape from the search box
        Assert.That(vm.IsSearchBarVisible, Is.False, "Escape returns them to hidden");
    }

    [Test]
    public async Task Escape_Leaves_The_Bars_Up_When_They_Are_Configured_To_Always_Show()
    {
        System.IO.File.WriteAllText(_settingsFile, """{ "SearchBarVisible": true }""");
        var vm = CreateShell(new FakeFileSystem(), new RecordingTrash());
        await vm.InitializeAsync();

        vm.FocusSearchCommand.Execute(null);
        vm.CollapseSearchBar();

        Assert.That(vm.IsSearchBarVisible, Is.True,
            "Escape collapses only a bar that was revealed on demand");
    }

    [Test]
    public async Task Inspector_Summarises_A_Multi_Selection_Instead_Of_Previewing_One_Item()
    {
        var fs = new FakeFileSystem();
        fs.Entries.Add(File("a.txt"));
        fs.Entries.Add(File("b.txt"));
        fs.Entries.Add(Dir("sub"));
        var vm = CreateShell(fs, new RecordingTrash());
        await vm.InitializeAsync();

        // Mirror what the list actually does: click one row, then Ctrl-click to extend. The primary
        // SelectedEntry stays on the first row, so only SetSelection reports the growth.
        var tab = vm.ActiveTab!;
        tab.SelectedEntry = tab.Entries.First();
        tab.SetSelection([tab.Entries.First()]);
        Assert.That(await WaitUntilAsync(() => vm.Preview is not null), Is.True);

        tab.SetSelection([.. tab.Entries]);

        Assert.That(await WaitUntilAsync(() => vm.Preview?.Title == "3 items selected"), Is.True,
            "extending the selection switches the inspector to the combined summary");
        Assert.That(vm.Preview!.Text, Does.Contain("(2 file(s), 1 folder(s))"));
    }

    [Test]
    public async Task Inspector_Returns_To_A_Single_Preview_When_The_Selection_Shrinks()
    {
        var fs = new FakeFileSystem();
        fs.Entries.Add(File("a.txt"));
        fs.Entries.Add(File("b.txt"));
        var vm = CreateShell(fs, new RecordingTrash());
        await vm.InitializeAsync();

        var tab = vm.ActiveTab!;
        tab.SelectedEntry = tab.Entries.First();
        tab.SetSelection([.. tab.Entries]);
        Assert.That(await WaitUntilAsync(() => vm.Preview?.Title == "2 items selected"), Is.True);

        tab.SetSelection([tab.Entries.First()]); // Ctrl-click the second row off again

        Assert.That(await WaitUntilAsync(() => vm.Preview?.Title != "2 items selected"), Is.True,
            "dropping back to one item restores the normal per-file preview");
    }

    [Test]
    public async Task Inspector_Previews_The_Single_Item_When_Only_One_Is_Selected()
    {
        var fs = new FakeFileSystem();
        fs.Entries.Add(File("only.txt"));
        var vm = CreateShell(fs, new RecordingTrash());
        await vm.InitializeAsync();

        var tab = vm.ActiveTab!;
        tab.SetSelection([tab.Entries.First()]);
        tab.SelectedEntry = tab.Entries.First();

        Assert.That(await WaitUntilAsync(() => vm.Preview is not null), Is.True);
        Assert.That(vm.Preview!.Title, Is.Not.EqualTo("1 items selected"),
            "a single selection still gets the normal per-file preview");
    }
}
