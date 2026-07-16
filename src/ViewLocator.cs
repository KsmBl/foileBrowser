using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using FoileBrowser.ViewModels;

namespace FoileBrowser;

/// <summary>
/// Resolves a view for a view-model via an explicit, trim/AOT-safe map (no reflection). Most views
/// are wired through explicit <c>DataTemplate</c>s or are Windows; this locator is only the fallback
/// for any <see cref="ViewModelBase"/> placed directly in content — add entries here as needed.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Func<Control>> Views = new()
    {
        // [typeof(SomeViewModel)] = static () => new SomeView(),
    };

    public Control? Build(object? param) =>
        param is not null && Views.TryGetValue(param.GetType(), out var factory) ? factory() : null;

    public bool Match(object? data) => data is ViewModelBase && Views.ContainsKey(data.GetType());
}
