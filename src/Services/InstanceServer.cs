using System.Net.Sockets;
using System.Text;

namespace FoileBrowser.Services;

/// <summary>
/// Makes the browser a single process serving every window (PRD §6.12).
///
/// Launching a second copy costs another whole runtime — heap, JIT or AOT image, and the toolkit's
/// managed state — for a window that shares none of it. Handing the request to the copy already
/// running instead means the second window costs only its own surface and control tree, which is
/// how the desktop's own file managers stay cheap across many windows.
///
/// The channel is a Unix domain socket under the user's runtime directory, so it is private to the
/// user and disappears with the session. A stale socket left by a killed process is detected by
/// trying to connect and taking over when nothing answers.
/// </summary>
public sealed class InstanceServer : IDisposable
{
    private readonly Socket _listener;
    private readonly string _path;
    private CancellationTokenSource? _accepting;

    private InstanceServer(Socket listener, string path)
    {
        _listener = listener;
        _path = path;
    }

    /// <summary>Raised on a worker thread with the path a newly launched copy asked us to open.</summary>
    public event EventHandler<string>? OpenRequested;

    /// <summary>
    /// Becomes the serving instance, or returns null when another copy already is — in which case
    /// <paramref name="handedOver"/> says whether it accepted our request and we should just exit.
    /// </summary>
    public static InstanceServer? Claim(string? requestedPath, out bool handedOver)
    {
        handedOver = false;
        var path = SocketPath();
        if (path is null)
            return null; // no private runtime directory: run standalone rather than guess

        try
        {
            if (TrySend(path, requestedPath))
            {
                handedOver = true;
                return null;
            }

            // Nothing answered, so any socket file here is a leftover from a process that died.
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or SocketException or UnauthorizedAccessException)
        {
            // Fall through and try to listen; if that fails too we simply run standalone.
        }

        try
        {
            var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(path));
            listener.Listen(4);
            return new InstanceServer(listener, path);
        }
        catch (Exception ex) when (ex is IOException or SocketException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Starts answering later launches. Callers marshal the event onto the UI thread.</summary>
    public void Start()
    {
        _accepting = new CancellationTokenSource();
        var token = _accepting.Token;
        _ = Task.Run(() => this.AcceptAsync(token), token);
    }

    private async Task AcceptAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await _listener.AcceptAsync(token);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or OperationCanceledException)
            {
                return;
            }

            try
            {
                using (client)
                {
                    var buffer = new byte[4096];
                    var read = await client.ReceiveAsync(buffer, SocketFlags.None, token);
                    var request = Encoding.UTF8.GetString(buffer, 0, read).Trim();
                    await client.SendAsync("ok"u8.ToArray(), SocketFlags.None, token);
                    this.OpenRequested?.Invoke(this, request);
                }
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or OperationCanceledException)
            {
                // A launcher that gave up mid-handshake is not our problem; keep serving.
            }
        }
    }

    /// <summary>Asks an already-running instance to open a window; false when nobody is listening.</summary>
    private static bool TrySend(string path, string? requestedPath)
    {
        if (!File.Exists(path))
            return false;

        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            client.Connect(new UnixDomainSocketEndPoint(path));
        }
        catch (SocketException)
        {
            return false; // the socket file outlived the process that made it
        }

        client.Send(Encoding.UTF8.GetBytes(requestedPath ?? string.Empty));
        var reply = new byte[8];
        client.ReceiveTimeout = 3000;
        try
        {
            return client.Receive(reply) > 0;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>
    /// The socket for this user, keyed by the runtime directory the session owns. Falls back to the
    /// temp directory with the user id in the name, so two users never share one.
    /// </summary>
    private static string? SocketPath()
    {
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(runtime) || !Directory.Exists(runtime))
        {
            runtime = Path.Combine(Path.GetTempPath(), $"foilebrowser-{Environment.UserName}");
            try
            {
                Directory.CreateDirectory(runtime);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return Path.Combine(runtime, "foilebrowser.sock");
    }

    public void Dispose()
    {
        _accepting?.Cancel();
        _accepting?.Dispose();
        _listener.Dispose();
        try
        {
            File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The socket file goes with the session anyway.
        }
    }
}
