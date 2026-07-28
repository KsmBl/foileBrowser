using Hawkynt.NativeForms;

namespace FoileBrowser.Views;

/// <summary>
/// Confirms an irreversible overwrite-and-delete (PRD §6.3). The destructive button stays disabled
/// until the acknowledgement is ticked, and the caveat about what overwriting really destroys is
/// spelled out rather than implied.
/// </summary>
public sealed class ShredConfirmDialog : Form
{
    private const string Caveat =
        "Overwriting only reliably destroys the old data on a traditional filesystem on rotating media. " +
        "On SSDs (wear levelling, TRIM), copy-on-write filesystems such as btrfs and ZFS, and journalled, " +
        "compressed, RAID or network storage, the original blocks can survive this pass. Treat it as making " +
        "casual recovery hard, not as a guaranteed wipe.";

    public ShredConfirmDialog(IReadOnlyList<string> paths)
    {
        this.Text = "Delete permanently";
        this.Bounds = new(0, 0, 520, 400);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MinimizeBox = false;
        this.MaximizeBox = false;

        Ui.Outline(this);

        var shred = new Button { Text = "Overwrite and delete", Bounds = new(322, 330, 170, 30), Enabled = false };
        var cancel = new Button { Text = "Cancel", Bounds = new(222, 330, 90, 30) };
        var acknowledge = new CheckBox
        {
            Text = "I understand — delete these items permanently",
            Bounds = new(16, 296, 480, 24),
        };
        acknowledge.CheckedChanged += (_, _) => shred.Enabled = acknowledge.Checked;

        this.Controls.AddRange(
            new Label
            {
                Text = "Overwrite with zeroes and delete permanently?",
                Bounds = new(16, 14, 480, 24),
            },
            new TextBox
            {
                Text = Describe(paths),
                Bounds = new(16, 44, 480, 90),
                Multiline = true,
                ReadOnly = true,
                AcceptsReturn = true,
            },
            new Label
            {
                Text = "This does not go to the trash and cannot be undone.",
                Bounds = new(16, 142, 480, 22),
            },
            new TextBox
            {
                Text = Caveat,
                Bounds = new(16, 168, 480, 120),
                Multiline = true,
                ReadOnly = true,
                AcceptsReturn = true,
            },
            acknowledge,
            shred,
            cancel);

        shred.DialogResult = DialogResult.OK;
        this.CancelButton = cancel;
    }

    private static string Describe(IReadOnlyList<string> paths) => paths.Count switch
    {
        0 => "Nothing is selected.",
        1 => paths[0],
        <= 6 => string.Join(Environment.NewLine, paths),
        _ => string.Join(Environment.NewLine, paths.Take(5))
             + $"{Environment.NewLine}… and {paths.Count - 5} more ({paths.Count} items in total)",
    };

    public static Task<bool> RequestAsync(Form owner, IReadOnlyList<string> paths) =>
        Task.FromResult(new ShredConfirmDialog(paths).ShowDialog(owner) == DialogResult.OK);
}
