using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace FoileBrowser.Themes;

/// <summary>
/// The Windows XP "Luna" skin (PRD §6.8), added on top of <c>FluentTheme</c> when the user picks it.
/// Compiled XAML behind an explicit type, so no runtime XAML loading or reflection is involved.
/// </summary>
public partial class WindowsXpTheme : Styles
{
    public WindowsXpTheme() => AvaloniaXamlLoader.Load(this);
}
