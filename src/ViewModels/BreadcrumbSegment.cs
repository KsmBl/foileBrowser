namespace FoileBrowser.ViewModels;

/// <summary>One clickable segment of the breadcrumb path bar (PRD §6.1).</summary>
public sealed record BreadcrumbSegment(string Name, string Path, bool ShowSeparator = false);
