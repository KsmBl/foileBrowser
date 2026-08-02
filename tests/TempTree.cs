namespace FoileBrowser.Tests;

/// <summary>Best-effort removal of the temporary tree a fixture built for itself.</summary>
/// <remarks>
/// Tidying up must never be what fails a run, and catching <see cref="IOException"/> alone was not
/// enough to guarantee that: a settings write that has not landed yet still holds its
/// <c>.tmp</c> file, and on Windows a locked file inside the tree makes the recursive delete throw
/// <see cref="UnauthorizedAccessException"/> instead — so a passing test failed in its own TearDown.
/// A short retry lets the write finish; after that the directory is simply left behind, which costs
/// a temp folder and nothing else.
/// </remarks>
internal static class TempTree
{
    private const int Attempts = 10;
    private const int PauseMs = 20;

    internal static void Remove(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return;

        for (var attempt = 1; ; ++attempt)
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt == Attempts)
                    return;

                Thread.Sleep(PauseMs);
            }
    }
}
