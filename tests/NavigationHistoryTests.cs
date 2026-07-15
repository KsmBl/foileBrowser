using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

[TestFixture]
public class NavigationHistoryTests
{
    [Test]
    public void New_History_Cannot_Navigate()
    {
        var history = new NavigationHistory();

        Assert.Multiple(() =>
        {
            Assert.That(history.Current, Is.Null);
            Assert.That(history.CanGoBack, Is.False);
            Assert.That(history.CanGoForward, Is.False);
        });
    }

    [Test]
    public void Visiting_Sets_Current_And_Enables_Back_After_Second_Visit()
    {
        var history = new NavigationHistory();
        history.Visit("/a");

        Assert.That(history.Current, Is.EqualTo("/a"));
        Assert.That(history.CanGoBack, Is.False, "single entry has nowhere to go back to");

        history.Visit("/b");

        Assert.That(history.Current, Is.EqualTo("/b"));
        Assert.That(history.CanGoBack, Is.True);
    }

    [Test]
    public void Back_And_Forward_Traverse_The_Stack()
    {
        var history = new NavigationHistory();
        history.Visit("/a");
        history.Visit("/b");
        history.Visit("/c");

        Assert.That(history.GoBack(), Is.EqualTo("/b"));
        Assert.That(history.GoBack(), Is.EqualTo("/a"));
        Assert.That(history.CanGoBack, Is.False);
        Assert.That(history.GoForward(), Is.EqualTo("/b"));
        Assert.That(history.GoForward(), Is.EqualTo("/c"));
        Assert.That(history.CanGoForward, Is.False);
    }

    [Test]
    public void Visiting_After_Going_Back_Truncates_Forward_History()
    {
        var history = new NavigationHistory();
        history.Visit("/a");
        history.Visit("/b");
        history.Visit("/c");
        history.GoBack(); // now at /b

        history.Visit("/d");

        Assert.That(history.Current, Is.EqualTo("/d"));
        Assert.That(history.CanGoForward, Is.False, "forward entry /c should be dropped");
        Assert.That(history.GoBack(), Is.EqualTo("/b"));
    }

    [Test]
    public void Revisiting_Current_Path_Is_A_Noop()
    {
        var history = new NavigationHistory();
        history.Visit("/a");
        history.Visit("/b");

        history.Visit("/b"); // e.g. a refresh

        Assert.That(history.CanGoForward, Is.False);
        Assert.That(history.GoBack(), Is.EqualTo("/a"));
    }
}
