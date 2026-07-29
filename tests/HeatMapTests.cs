using System.Drawing;
using FoileBrowser.Models;
using FoileBrowser.ViewModels;
using FoileBrowser.Views;

namespace FoileBrowser.Tests;

/// <summary>
/// Heat maps (PRD §6.1): tinting a column's cells by what its values say — ranked between the
/// folder's smallest and largest for a measurement, grouped by equal value for a name.
/// </summary>
[TestFixture]
public class HeatMapTests
{
    private static readonly Color Light = Color.FromArgb(255, 255, 255);
    private static readonly Color Dark = Color.FromArgb(30, 30, 30);

    // ---- the colour scale ----

    [Test]
    public void A_Ranked_Value_Runs_Cold_At_The_Bottom_And_Hot_At_The_Top()
    {
        var cold = Heat.Numeric(0, 0, 100, Light)!.Value;
        var hot = Heat.Numeric(100, 0, 100, Light)!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(cold.B, Is.GreaterThan(cold.R), "the bottom of the range leans blue");
            Assert.That(hot.R, Is.GreaterThan(hot.B), "the top of the range leans red");
        });
    }

    [Test]
    public void A_Folder_Where_Every_Value_Is_The_Same_Gets_No_Gradient()
    {
        // A ramp across an empty range would draw a distinction the data does not make.
        Assert.That(Heat.Numeric(5, 5, 5, Light), Is.Null);
    }

    [Test]
    public void A_Cell_With_Nothing_To_Rank_Is_Left_Alone()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Heat.Numeric(null, 0, 100, Light), Is.Null, "an unmeasured folder or pending value");
            Assert.That(Heat.Category(null, Light), Is.Null);
            Assert.That(Heat.Category(string.Empty, Light), Is.Null, "an absence is not a category");
        });
    }

    [Test]
    public void The_Tint_Leans_The_Background_Rather_Than_Replacing_It()
    {
        // Which is what lets one scale serve a light desktop and a dark one: on white the tint stays
        // bright, on near-black it stays dark, and the theme's own text stays readable on both.
        var onLight = Heat.Numeric(100, 0, 100, Light)!.Value;
        var onDark = Heat.Numeric(100, 0, 100, Dark)!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(Brightness(onLight), Is.GreaterThan(Brightness(onDark)));
            Assert.That(Brightness(onLight), Is.GreaterThan(0.5), "still a light background");
            Assert.That(Brightness(onDark), Is.LessThan(0.5), "still a dark background");
        });
    }

    [Test]
    public void The_Same_Value_Is_Always_The_Same_Colour()
    {
        // The colour has to mean the same thing tomorrow, so the hash behind it cannot be the
        // per-process-seeded one.
        var first = Heat.Category("jpg", Light);
        var again = Heat.Category("jpg", Light);

        Assert.Multiple(() =>
        {
            Assert.That(again, Is.EqualTo(first), "asking twice gives the same answer");
            Assert.That(Heat.Category("JPG", Light), Is.EqualTo(first), "case is not a category");
            Assert.That(Heat.Category("png", Light), Is.Not.EqualTo(first));
        });
    }

    private static double Brightness(Color c) => ((0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B)) / 255.0;

    // ---- what each column ranks by ----

    private static FileEntryViewModel Entry(string name, long? size = null, DateTimeOffset? modified = null)
        => new(new FileSystemEntry
        {
            Name = name,
            FullPath = "/tmp/" + name,
            Kind = FileSystemEntryKind.File,
            Size = size,
            Modified = modified,
        });

    [Test]
    public void A_Files_Size_And_Date_Rank_By_Their_Real_Values()
    {
        var entry = Entry("a.txt", size: 4096, modified: DateTimeOffset.UnixEpoch.AddDays(10));

        Assert.Multiple(() =>
        {
            Assert.That(entry.GetHeatValue("size"), Is.EqualTo(4096));
            Assert.That(entry.GetHeatValue("modified"), Is.EqualTo(DateTimeOffset.UnixEpoch.AddDays(10).Ticks));
        });
    }

    [Test]
    public void The_Columns_That_Group_Rather_Than_Rank_Offer_No_Number()
    {
        var entry = Entry("a.txt", size: 10);

        Assert.Multiple(() =>
        {
            Assert.That(entry.GetHeatValue("name"), Is.Null);
            Assert.That(entry.GetHeatValue("type"), Is.Null);
            Assert.That(entry.GetHeatValue("extension"), Is.Null);
        });
    }

    [Test]
    public void A_Metadata_Column_Ranks_By_The_First_Number_In_What_It_Shows()
    {
        // Metadata has only ever produced display text, so "1920×1080" ranks by its width and
        // "5.2 Mbps" by its rate — the same reading for every row, so the order is the shown order.
        var entry = Entry("clip.mp4");
        entry.Metadata = (_, id) => id switch
        {
            "dimensions" => "1920×1080",
            "bitrate" => "5.2 Mbps",
            "duration" => "00:03:20",
            "codec" => "hevc",
            _ => string.Empty,
        };

        Assert.Multiple(() =>
        {
            Assert.That(entry.GetHeatValue("dimensions"), Is.EqualTo(1920));
            Assert.That(entry.GetHeatValue("bitrate"), Is.EqualTo(5.2).Within(0.001));
            Assert.That(entry.GetHeatValue("duration"), Is.EqualTo(0), "leading zeros still parse");
            Assert.That(entry.GetHeatValue("codec"), Is.Null, "no digits, nothing to rank by");
            Assert.That(entry.GetHeatValue("missing"), Is.Null, "a column that does not apply here");
        });
    }

    [Test]
    public void A_Name_That_Happens_To_Contain_A_Number_Is_Never_Ranked_By_It()
    {
        // "h264" would read as 264, which ranks codecs by nothing at all. It never comes up: a
        // column declares how it heats, and the ones whose values are names group instead of rank —
        // so their numeric value is never asked for. This pins the half that keeps it that way.
        var media = ColumnCatalog.All.Where(c => c.Kind is not ColumnKind.Builtin).ToList();
        Assume.That(media, Is.Not.Empty, "metadata columns are registered by the shell");

        foreach (var column in media)
            Assert.That(
                column.Heat,
                Is.EqualTo(column.RightAligned ? HeatKind.Numeric : HeatKind.Category),
                $"\"{column.Id}\" heats the way its values read");
    }

    // ---- which columns are heated ----

    [Test]
    public void Only_Columns_With_Something_To_Say_Can_Be_Heated()
    {
        var byId = ColumnCatalog.All.ToDictionary(c => c.Id, c => c.Heat);

        Assert.Multiple(() =>
        {
            Assert.That(byId["size"], Is.EqualTo(HeatKind.Numeric));
            Assert.That(byId["modified"], Is.EqualTo(HeatKind.Numeric));
            Assert.That(byId["type"], Is.EqualTo(HeatKind.Category));
            Assert.That(byId["extension"], Is.EqualTo(HeatKind.Category));
            Assert.That(byId["name"], Is.EqualTo(HeatKind.None), "every name is distinct — a colour per row is noise");
        });
    }
}
