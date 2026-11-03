using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using static GodotXaml.Core.Conversion.DynamicPropertyFamilies;

namespace GodotXaml.Core.Tests.Conversion;

// Each row of the dynamic-family table states how Godot creates its entries; these check every claim against its pattern
// and the metadata, so a row cannot be added without one.
public sealed class DynamicPropertyFamiliesTests
{
    private static readonly GodotMetadata Metadata = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    [Fact]
    public void EveryFamilyPatternIsAnchored() => Assert.Empty(Offenders(row => !row.Family.Pattern.ToString().StartsWith('^')));

    [Fact]
    public void EveryFamilyClassIsAGodotClass() => Assert.Empty(Offenders(row => Metadata.FindClass(row.Class) is null));

    [Fact]
    public void AnOnKeyFamilyNamesItsEntryAndKeyOrder()
    {
        Assert.Empty(
            Offenders(row =>
                row.Family.Creation == EntryCreation.OnKey
                && (
                    !Has(row.Family, "index")
                    || row.Family.SizedBy is not null
                    || (
                        Has(row.Family, "key")
                            ? row.Family.KeyOrder.IsEmpty
                                || row.Family.KeyOrder.Distinct(StringComparer.Ordinal).Count() != row.Family.KeyOrder.Length
                            : !row.Family.KeyOrder.IsEmpty
                    )
                )
            )
        );
    }

    [Fact]
    public void ASizedFirstFamilyIsSizedByAPropertyRankedBeforeIt()
    {
        List<(string Class, DynamicFamily Family)> rows = [.. All];
        List<string> offenders = [];
        for (int index = 0; index < rows.Count; index++)
        {
            (string cls, DynamicFamily family) = rows[index];
            if (family.Creation == EntryCreation.SizedFirst && !IsSizedFirst(rows, index))
            {
                offenders.Add($"{cls} {family.Pattern} sized by {family.SizedBy ?? "null"}");
            }
        }

        Assert.Empty(offenders);
    }

    // Whether the row names a count property the metadata lists for its class or an ancestor, or an earlier row of its class
    // matches in full, and has no entry order of its own.
    private static bool IsSizedFirst(List<(string Class, DynamicFamily Family)> rows, int index)
    {
        (string cls, DynamicFamily family) = rows[index];
        bool sized =
            family.SizedBy is { } sizedBy
            && (IsListed(cls, sizedBy) || rows.Take(index).Any(earlier => earlier.Class == cls && MatchesInFull(earlier.Family, sizedBy)));
        return sized && !Has(family, "index") && !Has(family, "key") && family.KeyOrder.IsEmpty;
    }

    [Fact]
    public void AnAnyOrderFamilyHasNoEntryOrder()
    {
        Assert.Empty(
            Offenders(row =>
                row.Family.Creation == EntryCreation.AnyOrder
                && (Has(row.Family, "index") || Has(row.Family, "key") || !row.Family.KeyOrder.IsEmpty || row.Family.SizedBy is not null)
            )
        );
    }

    // The rows that fail the check, each named by its class and pattern.
    private static List<string> Offenders(Func<(string Class, DynamicFamily Family), bool> fails) =>
        [.. All.Where(fails).Select(row => $"{row.Class} {row.Family.Pattern}")];

    private static bool Has(DynamicFamily family, string group) => family.Pattern.GetGroupNames().Contains(group, StringComparer.Ordinal);

    // Whether the metadata lists the property for the class or one of its ancestors.
    private static bool IsListed(string cls, string property) =>
        Metadata.FindClass(cls) is { } info && Metadata.FindProperty(info, property) is not null;

    private static bool MatchesInFull(DynamicFamily family, string name) =>
        family.Pattern.Match(name) is { Success: true } match && match.Value == name;
}
