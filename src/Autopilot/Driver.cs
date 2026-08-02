using System.Diagnostics;
using System.Drawing;
using FoileBrowser.Views;
using Hawkynt.NativeForms;

namespace FoileBrowser.Autopilot;

/// <summary>
/// Drives the real window with real GTK events — presses, keys, drags — and reports what each
/// gesture actually did.
/// </summary>
/// <remarks>
/// <para>
/// A file manager is almost entirely gestures, and a gesture is the one thing a headless test cannot
/// have: the toolkit fakes raise events directly, so they prove the handler and never the path from
/// a press on a pixel to that handler. Everything here goes through <c>gtk_main_do_event</c>, which
/// is the entry point the GDK backend itself calls, so widget lookup, grabs and propagation are the
/// real ones.
/// </para>
/// <para>
/// Run with <c>--autopilot</c>. Every check names what it expected in the terms a person would use,
/// because the output is read when something is broken and not otherwise.
/// </para>
/// </remarks>
internal sealed partial class Driver
{
    /// <summary>How long a marshalled gesture may take before the UI thread is called wedged.</summary>
    private const int _StepTimeoutMs = 20_000;

    private readonly MainForm _form;
    private readonly List<string> _failures = [];
    private readonly List<string> _notes = [];
    private nint _root;
    private int _passed;
    private string _check = string.Empty;

    private Driver(MainForm form) => _form = form;

    private sealed class WedgedException(string what)
        : Exception($"the UI thread did not come back within {_StepTimeoutMs} ms during {what}");

    /// <summary>Runs the walkthrough against a live window and reports whether everything held.</summary>
    internal static int Run(MainForm form, string root)
    {
        var driver = new Driver(form);
        var watch = Stopwatch.StartNew();
        try
        {
            driver.Pump("locating the main window", () => driver._root = Injection.MainWindow("foileBrowser"));
            driver.Walk(root);
        }
        catch (Exception e)
        {
            driver._failures.Add($"the walkthrough stopped early: {e.Message}");
        }

        Console.WriteLine();
        foreach (var note in driver._notes)
            Console.WriteLine($"  note: {note}");

        if (driver._failures.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Broken:");
            foreach (var failure in driver._failures)
                Console.WriteLine($"  - {failure}");
        }

        Console.WriteLine();
        Console.WriteLine(
            $"autopilot: {driver._passed + driver._failures.Count} checks, {driver._passed} passed, "
            + $"{driver._failures.Count} failed in {watch.Elapsed.TotalSeconds:0.0} s");

        return driver._failures.Count == 0 ? 0 : 1;
    }

    // --- Reporting ---------------------------------------------------------------------------------

    private void Check(string what, Action body)
    {
        _check = what;
        try
        {
            body();
            Console.WriteLine($"PASS  {what}");
            ++_passed;
        }
        catch (ExpectationFailed e)
        {
            Console.WriteLine($"FAIL  {what}");
            Console.WriteLine($"        {e.Message}");
            _failures.Add($"{what} — {e.Message}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"FAIL  {what}");
            Console.WriteLine($"        threw {e.GetType().Name}: {e.Message}");
            _failures.Add($"{what} — threw {e.GetType().Name}: {e.Message}");
        }
    }

    private sealed class ExpectationFailed(string message) : Exception(message);

    private static void Expect(string what, object? observed, object? wanted)
    {
        if (!Equals(observed, wanted))
            throw new ExpectationFailed($"{what}: expected {Show(wanted)}, observed {Show(observed)}");
    }

    private static void ExpectTrue(string what, bool observed)
    {
        if (!observed)
            throw new ExpectationFailed(what);
    }

    private static string Show(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        _ => value.ToString() ?? "?",
    };

    /// <summary>Records something worth knowing that is not a failure.</summary>
    private void Note(string what) => _notes.Add($"{_check}: {what}");

    // --- Marshalling -------------------------------------------------------------------------------

    /// <summary>Runs an action on the UI thread and waits for it, so a gesture is finished before the
    /// next line reads what it did.</summary>
    /// <summary>True while this thread is running inside a pumped action — that is, on the UI thread.</summary>
    [ThreadStatic]
    private static bool _onUiThread;

    private void Pump(string what, Action action)
    {
        // A pump reached from inside a pump is already on the UI thread. Posting again and waiting
        // would block that thread against a message only it can deliver, and the walkthrough would
        // hang on its own convenience helpers.
        if (_onUiThread)
        {
            action();
            return;
        }

        var done = new ManualResetEventSlim(false);
        Exception? error = null;
        _form.BeginInvoke(() =>
        {
            _onUiThread = true;
            try
            {
                action();
            }
            catch (Exception e)
            {
                error = e;
            }
            finally
            {
                _onUiThread = false;
                done.Set();
            }
        });

        if (!done.Wait(_StepTimeoutMs))
            throw new WedgedException(what);
        if (error is not null)
            throw error;
    }

    private T Read<T>(Func<T> read)
    {
        var result = default(T)!;
        this.Pump("reading state", () => result = read());
        return result;
    }

    /// <summary>Lets queued work and repaints finish before anything is read back.</summary>
    private void Settle(int quietMs = 60)
    {
        this.Pump("settling", Injection.Drain);
        Thread.Sleep(quietMs);
        this.Pump("settling", Injection.Drain);
    }

    /// <summary>Waits for something to become true, so an asynchronous listing is not a race.</summary>
    private bool Until(Func<bool> ready, int timeoutMs = 5000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (this.Read(ready))
                return true;

            this.Settle(30);
        }

        return this.Read(ready);
    }

    // --- Gestures ----------------------------------------------------------------------------------

    private Point ScreenOf(Control control, int dx, int dy)
        => this.Read(() => control.PointToScreen(new Point(dx, dy)));

    private void ClickAt(Point screen, uint button = 1, uint modifiers = 0)
    {
        this.Pump("a click", () =>
        {
            Injection.Move(_root, screen);
            Injection.Press(_root, screen, button, modifiers);
            Injection.Release(_root, screen, button, modifiers);
        });
        this.Settle();
    }

    private void Click(Control control, int dx, int dy, uint button = 1, uint modifiers = 0)
        => this.ClickAt(this.ScreenOf(control, dx, dy), button, modifiers);

    private void DoubleClick(Control control, int dx, int dy)
    {
        var screen = this.ScreenOf(control, dx, dy);
        this.ClickAt(screen);
        this.Pump("a double click", () =>
        {
            Injection.Press(_root, screen, 1, 0);
            Injection.Press(_root, screen, 1, 0, doubleClick: true);
            Injection.Release(_root, screen, 1, 0);
        });
        this.Settle();
    }

    /// <summary>Presses, drags in steps under the implicit grab, and releases.</summary>
    private void Drag(Control control, Point from, Point to, int steps = 10)
    {
        var start = this.ScreenOf(control, from.X, from.Y);
        var end = this.ScreenOf(control, to.X, to.Y);
        this.Pump("a drag press", () =>
        {
            Injection.Move(_root, start);
            Injection.Press(_root, start, 1, 0);
        });

        for (var step = 1; step <= steps; ++step)
        {
            var at = new Point(
                start.X + ((end.X - start.X) * step / steps),
                start.Y + ((end.Y - start.Y) * step / steps));
            this.Pump("a drag move", () => Injection.Move(_root, at, buttonHeld: true));
        }

        this.Pump("a drag release", () => Injection.Release(_root, end, 1, 0));
        this.Settle(120);
    }

    private void Key(Keys key, uint modifiers = 0)
    {
        this.Pump($"the {key} key", () => Injection.Key(_root, KeyVal(key), modifiers));
        this.Settle();
    }

    private void Type(string text)
    {
        foreach (var ch in text)
            this.Pump($"typing {ch}", () => Injection.Key(_root, ch, 0));

        this.Settle();
    }
}
