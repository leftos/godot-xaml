using System.Collections.Immutable;
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

    // An index is ASCII digits only: \d would also match another script's digits, which Godot never writes.
    [Fact]
    public void NoFamilyPatternUsesDigitClass() =>
        Assert.Empty(Offenders(row => row.Family.Pattern.ToString().Contains(@"\d", StringComparison.Ordinal)));

    // The table is written once for every bundled version, so each of its class names must exist in each of them.
    [Fact]
    public void EveryFamilyClassIsAGodotClass()
    {
        List<string> offenders = [];
        foreach (string version in GodotMetadata.BundledVersions)
        {
            var metadata = GodotMetadata.LoadBundled(version);
            offenders.AddRange(Offenders(row => metadata.FindClass(row.Class) is null).Select(row => $"Godot {version} {row}"));
        }

        Assert.Empty(offenders);
    }

    // A class-list name ranks by the class list before any family, so a row over one would be shadowed; no row names one.
    [Fact]
    public void NoFamilyPatternMatchesAClassListName()
    {
        List<string> offenders = [];
        foreach (string version in GodotMetadata.BundledVersions)
        {
            var metadata = GodotMetadata.LoadBundled(version);
            foreach (GodotClassInfo cls in metadata.Classes)
            {
                List<(string Class, DynamicFamily Family)> families =
                [
                    .. metadata.ChainOf(cls).SelectMany(declaring => Of(declaring.Name).Select(family => (declaring.Name, family))),
                ];
                foreach (string name in cls.ListOrder)
                {
                    offenders.AddRange(
                        families
                            .Where(row => row.Family.Pattern.IsMatch(name))
                            .Select(row => $"Godot {version} {cls.Name} {name} {row.Class} {row.Family.Pattern}")
                    );
                }
            }
        }

        Assert.Empty(offenders);
    }

    // Godot writes an index in ASCII digits; \d would also match a non-ASCII digit, such as an Arabic-Indic three.
    [Fact]
    public void AFamilyIndexMatchesAsciiDigitsOnly()
    {
        (string Class, string Ascii, string NonAscii)[] cases =
        [
            ("TileMap", "layer_3/name", "layer_٣/name"),
            ("Animation", "tracks/3/type", "tracks/٣/type"),
            ("AudioEffectEQ", "band_db/3_hz", "band_db/٣_hz"),
            ("TileSetAtlasSource", "0:3/size_in_atlas", "0:٣/size_in_atlas"),
            ("Skeleton3D", "bones/3/name", "bones/٣/name"),
        ];
        foreach ((string cls, string ascii, string nonAscii) in cases)
        {
            DynamicFamily family = Assert.Single(Of(cls), row => row.Pattern.IsMatch(ascii));
            Assert.False(family.Pattern.IsMatch(nonAscii), $"{cls} matched {nonAscii}");
        }
    }

    // A family with a literal is matched by an ordinal comparison in the ranker, so that comparison and its pattern must agree.
    [Fact]
    public void ALiteralFamilyMatchesAsItsPatternDoes()
    {
        Assert.Contains(All, row => row.Family.Literal is not null);
        List<string> offenders = [];
        foreach ((string cls, DynamicFamily family) in All)
        {
            if (family.Literal is not { } literal)
            {
                continue;
            }

            string text = literal.Text;
            string different = text[..^1] + (text[^1] == 'x' ? 'y' : 'x');
            string[] names = [text, text + "x", different, string.Empty, text + "\n"];
            foreach (string name in names)
            {
                if (PropertyOrder.MatchesLiteral(literal, name) != family.Pattern.IsMatch(name))
                {
                    offenders.Add($"{cls} {family.Pattern} {name}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void AnOnKeyFamilyNamesItsEntryAndKeyOrder()
    {
        Assert.Empty(
            Offenders(row =>
                row.Family.Creation == EntryCreation.OnKey
                && (!Has(row.Family, "index") || row.Family.SizedBy is not null || IsNested(row.Family) || !HasKeyOrder(row.Family))
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
    // matches in full; and either has no entry order of its own, or names its entries and keys with a distinct key order.
    private static bool IsSizedFirst(List<(string Class, DynamicFamily Family)> rows, int index)
    {
        (string cls, DynamicFamily family) = rows[index];
        bool sized =
            family.SizedBy is { } sizedBy
            && (IsListed(cls, sizedBy) || rows.Take(index).Any(earlier => earlier.Class == cls && MatchesInFull(earlier.Family, sizedBy)));
        return sized && IsOrderedAsSized(family);
    }

    // A sized row with an index group names its keys with a distinct key order (and a distinct nested one); a row without
    // one has no keys, no nested entries and no orders.
    private static bool IsOrderedAsSized(DynamicFamily family) =>
        Has(family, "index")
            ? Has(family, "key") && HasKeyOrder(family) && IsDistinct(family.SubKeyOrder)
            : !Has(family, "key") && !IsNested(family) && family.KeyOrder.IsEmpty;

    // A nested entry is ordered inside its outer entry's key, so a row naming one names the outer entry and key too; a nested
    // key order needs a subkey group to read, while a row may name nested entries without keys (a list of collisions).
    [Fact]
    public void ANestedEntryNamesItsOuterEntry() =>
        Assert.Empty(
            Offenders(row =>
                ((Has(row.Family, "subindex") || Has(row.Family, "subkey")) && !(Has(row.Family, "index") && Has(row.Family, "key")))
                || (!row.Family.SubKeyOrder.IsEmpty && !Has(row.Family, "subkey"))
            )
        );

    [Fact]
    public void AnAnyOrderFamilyHasNoEntryOrder()
    {
        Assert.Empty(
            Offenders(row =>
                row.Family.Creation == EntryCreation.AnyOrder
                && (
                    Has(row.Family, "index")
                    || Has(row.Family, "key")
                    || IsNested(row.Family)
                    || !row.Family.KeyOrder.IsEmpty
                    || row.Family.SizedBy is not null
                )
            )
        );
    }

    // Whether the row names nested entries or keys, or carries a nested key order.
    private static bool IsNested(DynamicFamily family) => Has(family, "subindex") || Has(family, "subkey") || !family.SubKeyOrder.IsEmpty;

    // A row with a key group carries a non-empty, distinct key order; a row without one carries none.
    private static bool HasKeyOrder(DynamicFamily family) =>
        Has(family, "key") ? !family.KeyOrder.IsEmpty && IsDistinct(family.KeyOrder) : family.KeyOrder.IsEmpty;

    private static bool IsDistinct(ImmutableArray<string> keys) => keys.Distinct(StringComparer.Ordinal).Count() == keys.Length;

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
