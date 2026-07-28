using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using Hawkynt.NativeForms;

namespace FoileBrowser.Views;

/// <summary>
/// Small helpers shared by the views: property/collection subscriptions that keep the code
/// reflection-free (the toolkit has no reflection-bound binding surface), and the collapse gesture
/// for docked bars.
/// </summary>
internal static class Ui
{
    /// <summary>Runs <paramref name="onChanged"/> whenever one of <paramref name="properties"/> changes, and once now.</summary>
    public static Action Watch(INotifyPropertyChanged source, Action onChanged, params string[] properties)
    {
        void Handler(object? sender, PropertyChangedEventArgs e)
        {
            if (properties.Length == 0 || Array.IndexOf(properties, e.PropertyName) >= 0 || e.PropertyName is null)
                onChanged();
        }

        source.PropertyChanged += Handler;
        onChanged();
        return () => source.PropertyChanged -= Handler;
    }

    /// <summary>Runs <paramref name="onChanged"/> whenever the collection changes, and once now.</summary>
    public static Action WatchList(INotifyCollectionChanged source, Action onChanged)
    {
        var unsubscribe = ObserveList(source, onChanged);
        onChanged();
        return unsubscribe;
    }

    /// <summary>Subscribes without the initial call — for handlers that themselves cause the change.</summary>
    public static Action ObserveList(INotifyCollectionChanged source, Action onChanged)
    {
        void Handler(object? sender, NotifyCollectionChangedEventArgs e) => onChanged();

        source.CollectionChanged += Handler;
        return () => source.CollectionChanged -= Handler;
    }

    /// <summary>
    /// Shows or hides a docked bar. The layout engine reserves a docked child's size whether or not
    /// it is visible (the Windows Forms engine does the same), so collapsing means zeroing the
    /// extent along the docked axis as well as clearing <see cref="Control.Visible"/>.
    /// </summary>
    public static void SetDockedExtent(Control control, bool visible, int extent)
    {
        var size = visible ? extent : 0;
        var bounds = control.Bounds;
        control.Bounds = control.Dock is DockStyle.Left or DockStyle.Right
            ? new Rectangle(bounds.X, bounds.Y, size, bounds.Height)
            : new Rectangle(bounds.X, bounds.Y, bounds.Width, size);
        control.Visible = visible;
        control.Parent?.PerformLayout();
    }

    /// <summary>
    /// Draws a border around a dialog's content (PRD §6.8). Tiling window managers add no decoration
    /// of their own, so without this a dialog blends into whatever is behind it. The panel is added
    /// first so it sits behind the content and fills whatever the form leaves.
    /// </summary>
    public static void Outline(Form dialog) =>
        dialog.Controls.Add(new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle });

    /// <summary>Parses a "#rrggbb" tag colour, or null when it is missing or malformed (PRD §6.7).</summary>
    public static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;

        var text = hex.AsSpan().TrimStart('#');
        if (text.Length == 8 && uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var argb))
            return Color.FromArgb(unchecked((int)argb));
        if (text.Length == 6 && uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return Color.FromArgb(unchecked((int)(0xFF000000 | rgb)));
        return null;
    }
}
