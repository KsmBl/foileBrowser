using CommunityToolkit.Mvvm.ComponentModel;
using FoileBrowser.Docking;

namespace FoileBrowser.Tests;

[TestFixture]
public class DockLayoutTests
{
    private sealed class Tab(string title) : ObservableObject, IDockable
    {
        public string Title { get; } = title;
    }

    private static (DockLayout Layout, DockPane Pane, Tab A) NewSinglePane()
    {
        var a = new Tab("a");
        var pane = new DockPane();
        pane.Tabs.Add(a);
        pane.ActiveTab = a;
        return (new DockLayout(pane), pane, a);
    }

    [Test]
    public void Split_Right_Creates_A_Horizontal_Split_With_The_New_Pane_After()
    {
        var (layout, pane, a) = NewSinglePane();
        var b = new Tab("b");

        layout.Split(b, pane, DockSide.Right);

        Assert.That(layout.Root, Is.InstanceOf<DockSplit>());
        var split = (DockSplit)layout.Root;
        Assert.That(split.Orientation, Is.EqualTo(DockOrientation.Horizontal));
        Assert.That(split.Children, Has.Count.EqualTo(2));
        Assert.That(((DockPane)split.Children[0]).Tabs, Does.Contain(a));
        Assert.That(((DockPane)split.Children[1]).Tabs, Does.Contain(b), "Right places the new pane after");
        Assert.That(layout.ActiveDockable, Is.SameAs(b));
    }

    [Test]
    public void Split_Top_Creates_A_Vertical_Split_With_The_New_Pane_Before()
    {
        var (layout, pane, a) = NewSinglePane();
        var b = new Tab("b");

        layout.Split(b, pane, DockSide.Top);

        var split = (DockSplit)layout.Root;
        Assert.That(split.Orientation, Is.EqualTo(DockOrientation.Vertical));
        Assert.That(((DockPane)split.Children[0]).Tabs, Does.Contain(b), "Top places the new pane before");
        Assert.That(((DockPane)split.Children[1]).Tabs, Does.Contain(a));
    }

    [Test]
    public void CloseTab_Emptying_A_Pane_Collapses_The_Split()
    {
        var (layout, pane, a) = NewSinglePane();
        var b = new Tab("b");
        layout.Split(b, pane, DockSide.Right);

        layout.CloseTab(b);

        Assert.That(layout.Root, Is.InstanceOf<DockPane>(), "the split collapses back to the remaining pane");
        Assert.That(((DockPane)layout.Root).Tabs, Does.Contain(a));
        Assert.That(layout.ActiveDockable, Is.SameAs(a));
    }

    [Test]
    public void MoveTab_Reorders_Within_A_Pane()
    {
        var (layout, pane, a) = NewSinglePane();
        var c = new Tab("c");
        pane.Tabs.Add(c); // a, c

        layout.MoveTab(c, pane, 0); // c, a

        Assert.That(pane.Tabs, Is.EqualTo(new IDockable[] { c, a }));
    }

    [Test]
    public void MoveTab_Between_Panes_Collapses_The_Emptied_Source()
    {
        var (layout, pane, a) = NewSinglePane();
        var b = new Tab("b");
        layout.Split(b, pane, DockSide.Right); // pane[a] | new[b]
        var target = layout.PaneOf(a)!;

        layout.MoveTab(b, target, target.Tabs.Count); // move b next to a; b's pane empties

        Assert.That(layout.Root, Is.InstanceOf<DockPane>());
        Assert.That(((DockPane)layout.Root).Tabs, Is.EqualTo(new IDockable[] { a, b }));
    }

    [Test]
    public void Capture_And_Restore_RoundTrip_The_Tree()
    {
        var (layout, pane, _) = NewSinglePane();
        var b = new Tab("b");
        layout.Split(b, pane, DockSide.Bottom); // vertical split: a over b

        var state = DockNodeState.Capture(layout.Root, t => ((Tab)t).Title);
        var restored = DockNodeState.Restore(state, key => new Tab(key));

        Assert.That(restored, Is.Not.Null);
        var split = (DockSplit)restored!.Root;
        Assert.That(split.Orientation, Is.EqualTo(DockOrientation.Vertical));
        Assert.That(split.Children.Cast<DockPane>().Select(p => p.Tabs[0].Title), Is.EqualTo(new[] { "a", "b" }));
    }
}
