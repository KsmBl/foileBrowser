using System.Reflection;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

/// <summary>The column catalogue (PRD §6.1) and the copy it hands out.</summary>
[TestFixture]
public class ColumnCatalogTests
{
    [Test]
    public void A_Created_Column_Carries_Everything_Its_Template_Declared()
    {
        // Create clones the template property by property, so a property added to ColumnSpec and not
        // added here is silently dropped — which is exactly how heat maps first shipped doing nothing
        // at all: every visible column came back with the default. Reflection rather than a list, so
        // the next property is covered without anyone remembering to.
        var skip = new HashSet<string>
        {
            nameof(ColumnSpec.Width), // deliberately reset to the default width on create
        };

        var properties = typeof(ColumnSpec)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && !skip.Contains(p.Name))
            .ToList();

        Assume.That(properties, Is.Not.Empty);

        foreach (var template in ColumnCatalog.All)
        {
            var created = ColumnCatalog.Create(template.Id);
            Assert.That(created, Is.Not.Null, template.Id);

            foreach (var property in properties)
                Assert.That(
                    property.GetValue(created),
                    Is.EqualTo(property.GetValue(template)),
                    $"\"{template.Id}\" lost {property.Name} on the way through Create");
        }
    }

    [Test]
    public void An_Unknown_Column_Id_Creates_Nothing()
        => Assert.That(ColumnCatalog.Create("no-such-column"), Is.Null);
}
