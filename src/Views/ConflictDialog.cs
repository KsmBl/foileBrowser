using System.Drawing;
using FoileBrowser.Models;
using Hawkynt.NativeForms;

namespace FoileBrowser.Views;

/// <summary>
/// Asks what to do about a name that already exists at the destination (PRD §6.3). Until this
/// existed a collision was silently auto-renamed, which is safe but not what anyone asked for.
///
/// The copy engine calls its resolver from a worker thread and waits for the answer, so
/// <see cref="Prompt"/> marshals onto the UI thread and blocks that worker while the dialog is up.
/// "Apply to all" remembers the decision for the rest of the operation.
/// </summary>
public sealed class ConflictDialog : Form
{
    private readonly CheckBox _applyToAll = new()
    {
        Text = "Do this for the rest of this operation",
        Bounds = new(16, 250, 460, 24),
    };

    private ConflictResolution _choice = ConflictResolution.Cancel;

    private ConflictDialog(ConflictRequest request)
    {
        this.Text = "That name already exists";
        this.Bounds = new(0, 0, 520, 340);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MinimizeBox = false;
        this.MaximizeBox = false;
        Ui.Outline(this);

        var overwrite = Choice("Overwrite", ConflictResolution.Overwrite, new(16, 288, 110, 30));
        var skip = Choice("Skip", ConflictResolution.Skip, new(134, 288, 90, 30));
        var rename = Choice("Keep both", ConflictResolution.Rename, new(232, 288, 110, 30));
        var cancel = Choice("Cancel", ConflictResolution.Cancel, new(390, 288, 90, 30));

        this.Controls.AddRange(
            new Label
            {
                Text = Path.GetFileName(request.DestinationPath),
                Bounds = new(16, 14, 480, 22),
            },
            new Label
            {
                Text = "already exists in the destination folder.",
                Bounds = new(16, 38, 480, 22),
                ForeColor = Color.Gray,
            },
            Describe("Moving or copying", request.SourcePath, 74),
            Describe("Already there", request.DestinationPath, 162),
            _applyToAll,
            overwrite,
            skip,
            rename,
            cancel);

        this.AcceptButton = rename;
        this.CancelButton = cancel;
    }

    private Button Choice(string text, ConflictResolution resolution, Rectangle bounds)
    {
        var button = new Button { Text = text, Bounds = bounds };
        button.Click += (_, _) =>
        {
            _choice = resolution;
            this.DialogResult = DialogResult.OK;
        };
        return button;
    }

    /// <summary>A file's path, size and modified time, so the two sides can actually be told apart.</summary>
    private static Panel Describe(string heading, string path, int top)
    {
        var panel = new Panel { Bounds = new(16, top, 480, 80), BorderStyle = BorderStyle.FixedSingle };
        var detail = "—";
        try
        {
            var info = new FileInfo(path);
            if (info.Exists)
                detail = $"{ValueFormat.Size(info.Length, SizeUnit.Binary)}   ·   "
                       + info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
            else if (Directory.Exists(path))
                detail = "folder";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // An unreadable side still gets a row; the user can still choose.
        }

        panel.Controls.AddRange(
            new Label { Text = heading, Bounds = new(10, 6, 460, 20), ForeColor = Color.Gray },
            new Label { Text = path, Bounds = new(10, 28, 460, 20) },
            new Label { Text = detail, Bounds = new(10, 50, 460, 20), ForeColor = Color.Gray });
        return panel;
    }

    /// <summary>
    /// A resolver for <see cref="ViewModels.OperationQueueViewModel.ConflictResolver"/>. Answers from
    /// the remembered decision when there is one, otherwise asks — on the UI thread, blocking the
    /// worker that called it.
    /// </summary>
    public sealed class Prompt
    {
        private readonly Func<ConflictRequest, (ConflictResolution Choice, bool ApplyToAll)> _ask;
        private ConflictResolution? _remembered;

        /// <summary>Asks through a real dialog owned by <paramref name="owner"/>.</summary>
        public Prompt(Form owner)
            : this(request =>
            {
                var choice = ConflictResolution.Cancel;
                var applyToAll = false;
                // The copy engine is waiting on a worker thread; hop to the UI thread and block it.
                owner.Invoke(() =>
                {
                    var dialog = new ConflictDialog(request);
                    var verdict = dialog.ShowDialog(owner);
                    choice = verdict == DialogResult.OK ? dialog._choice : ConflictResolution.Cancel;
                    applyToAll = dialog._applyToAll.Checked;
                });
                return (choice, applyToAll);
            })
        {
        }

        /// <summary>Asks through an arbitrary prompt — the seam the tests answer through.</summary>
        public Prompt(Func<ConflictRequest, (ConflictResolution Choice, bool ApplyToAll)> ask) => _ask = ask;

        /// <summary>Forgets "apply to all" — called between queued operations.</summary>
        public void Reset() => _remembered = null;

        public ConflictResolution Resolve(ConflictRequest request)
        {
            if (_remembered is { } remembered)
                return remembered;

            var (choice, applyToAll) = _ask(request);
            if (applyToAll)
                _remembered = choice;
            return choice;
        }
    }
}
