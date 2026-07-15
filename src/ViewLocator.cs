using Avalonia.Controls;
using Avalonia.Controls.Templates;
using FoileBrowser.ViewModels;

namespace FoileBrowser;

/// <summary>
/// Resolves a view for a given view model by naming convention
/// (FoileBrowser.ViewModels.FooViewModel -> FoileBrowser.Views.FooView).
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!
            .Replace("ViewModels", "Views", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        return type is not null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
