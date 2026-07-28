using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

/// <summary>Type-to-select in the file list (PRD §6.6).</summary>
[TestFixture]
public class TypeAheadTests
{
    private static readonly string[] Names =
        ["apple", "Banana", "berry", "blueberry", "cherry"];

    private static DateTime At(int milliseconds) => new DateTime(2026, 1, 1) + TimeSpan.FromMilliseconds(milliseconds);

    [Test]
    public void A_Letter_Jumps_To_The_First_Entry_Starting_With_It()
    {
        var search = new TypeAhead();

        Assert.That(search.Next('b', Names, current: -1, At(0)), Is.EqualTo(1), "Banana");
    }

    [Test]
    public void Matching_Ignores_Case()
    {
        var search = new TypeAhead();

        Assert.That(search.Next('B', Names, current: -1, At(0)), Is.EqualTo(1));
    }

    [Test]
    public void Repeating_A_Letter_Steps_Through_The_Entries_That_Start_With_It()
    {
        var search = new TypeAhead();
        var first = search.Next('b', Names, current: -1, At(0));
        var second = search.Next('b', Names, first, At(200));
        var third = search.Next('b', Names, second, At(400));

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(1), "Banana");
            Assert.That(second, Is.EqualTo(2), "berry");
            Assert.That(third, Is.EqualTo(3), "blueberry");
        });
    }

    [Test]
    public void Stepping_Through_Wraps_Around()
    {
        var search = new TypeAhead();
        search.Next('c', Names, current: -1, At(0)); // cherry, the last entry

        Assert.That(search.Next('c', Names, current: 4, At(200)), Is.EqualTo(4), "the only c wraps back to itself");
    }

    [Test]
    public void Typing_On_Extends_The_Search_Rather_Than_Stepping()
    {
        var search = new TypeAhead();
        var b = search.Next('b', Names, current: -1, At(0));
        var bl = search.Next('l', Names, b, At(150));

        Assert.Multiple(() =>
        {
            Assert.That(b, Is.EqualTo(1), "Banana");
            Assert.That(bl, Is.EqualTo(3), "blueberry — the prefix grew, it did not step");
            Assert.That(search.Prefix, Is.EqualTo("bl"));
        });
    }

    [Test]
    public void A_Pause_Starts_A_New_Search()
    {
        var search = new TypeAhead();
        search.Next('b', Names, current: -1, At(0));

        var afterPause = search.Next('c', Names, current: 1, At(5000));

        Assert.That(afterPause, Is.EqualTo(4), "cherry, not a search for \"bc\"");
        Assert.That(search.Prefix, Is.EqualTo("c"));
    }

    [Test]
    public void Nothing_Matching_Reports_No_Row()
    {
        var search = new TypeAhead();

        Assert.That(search.Next('z', Names, current: -1, At(0)), Is.EqualTo(-1));
    }

    [Test]
    public void An_Empty_List_Reports_No_Row()
    {
        Assert.That(new TypeAhead().Next('a', [], current: -1, At(0)), Is.EqualTo(-1));
    }

    [Test]
    public void Reset_Forgets_The_Prefix()
    {
        var search = new TypeAhead();
        search.Next('b', Names, current: -1, At(0));

        search.Reset();

        Assert.That(search.Prefix, Is.Empty);
        Assert.That(search.Next('b', Names, current: 1, At(50)), Is.EqualTo(2), "a fresh search steps from the row after");
    }
}
