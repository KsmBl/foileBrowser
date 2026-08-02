using FoileBrowser.Services;

namespace FoileBrowser.Tests;

[TestFixture]
public class TrashServiceTests
{
    [Test]
    [Platform("Linux", Reason = "Exercises the Linux trash (gio / XDG spec) path.")]
    public async Task Trash_Removes_File_From_Source_Location()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "foile-trash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        // Redirect the XDG trash into the sandbox so we never touch the user's real trash.
        var previousXdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(sandbox, "data"));

        try
        {
            var file = Path.Combine(sandbox, "victim.txt");
            await File.WriteAllTextAsync(file, "delete me");

            await new TrashService().TrashAsync(file);

            Assert.That(File.Exists(file), Is.False, "file should be moved out of its source location");
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", previousXdg);
            TempTree.Remove(sandbox);
        }
    }

    [Test]
    public void Trash_Missing_Path_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), "foile-nope-" + Guid.NewGuid().ToString("N"));

        Assert.CatchAsync<FileNotFoundException>(() => new TrashService().TrashAsync(missing));
    }
}
