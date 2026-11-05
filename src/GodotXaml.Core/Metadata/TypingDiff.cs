using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Metadata;

/// <summary>How a property's typing differs between two metadata versions.</summary>
public enum TypingChangeKind
{
    /// <summary>The class is in the older metadata and not in the newer one.</summary>
    ClassRemoved,

    /// <summary>The class declares the property in the older metadata and not in the newer one.</summary>
    PropertyRemoved,

    /// <summary>The Variant type the property's value is saved as changed.</summary>
    Type,

    /// <summary>The property's enum or bitfield type changed.</summary>
    EnumType,

    /// <summary>A member of the property's enum was added, removed, renamed or renumbered.</summary>
    EnumMembers,

    /// <summary>Whether the property's enum type is a bitfield changed.</summary>
    Bitfield,

    /// <summary>The property hint changed.</summary>
    Hint,

    /// <summary>The property hint string changed.</summary>
    HintString,

    /// <summary>Whether the property is saved to a scene file changed.</summary>
    Stored,

    /// <summary>A C# name resolves to a different property or type when the element's class is not known.</summary>
    CSharpNameAgreement,
}

/// <summary>One property's typing difference between two metadata versions.</summary>
/// <param name="Class">The Godot class name; empty for a <see cref="TypingChangeKind.CSharpNameAgreement"/>.</param>
/// <param name="Property">The property's Godot name, or the C# name for a <see cref="TypingChangeKind.CSharpNameAgreement"/>.</param>
/// <param name="Kind">What changed.</param>
/// <param name="Was">The old value in short readable text; empty when the old side has none.</param>
/// <param name="Becomes">The new value in short readable text; empty when the new side has none.</param>
public sealed record TypingChange(string Class, string Property, TypingChangeKind Kind, string Was, string Becomes);

/// <summary>Lists the properties whose typing differs between two metadata versions, for an upgrade to report.</summary>
public static class TypingDiff
{
    /// <summary>Every property whose typing changes from one metadata version to another.</summary>
    /// <param name="from">The older metadata.</param>
    /// <param name="to">The newer metadata.</param>
    /// <returns>
    /// One entry per removed class, removed property, changed type, enum type, enum member, bitfield, hint, hint string or
    /// stored flag, and per C# name the classes agree on in one version and not the other; ordered by class, then property,
    /// then kind.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="from"/> or <paramref name="to"/> is null.</exception>
    public static ImmutableArray<TypingChange> Between(GodotMetadata from, GodotMetadata to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        List<TypingChange> changes = [.. ClassChanges(from, to), .. AgreementChanges(from, to)];
        changes.Sort(Compare);
        return [.. changes];
    }

    // Each class of the older metadata against the same class of the newer one.
    private static IEnumerable<TypingChange> ClassChanges(GodotMetadata from, GodotMetadata to)
    {
        foreach (GodotClassInfo cls in from.Classes)
        {
            GodotClassInfo? other = to.FindClass(cls.Name);
            if (other is null)
            {
                yield return new TypingChange(cls.Name, "", TypingChangeKind.ClassRemoved, "", "");
                continue;
            }

            foreach (TypingChange change in PropertyChanges(cls, other, from, to))
            {
                yield return change;
            }
        }
    }

    // Every property the older metadata's class declares: removed, or compared with the newer metadata's own property.
    private static List<TypingChange> PropertyChanges(GodotClassInfo cls, GodotClassInfo other, GodotMetadata from, GodotMetadata to)
    {
        var declared = other.Properties.ToFrozenDictionary(p => p.Name, StringComparer.Ordinal);
        List<TypingChange> changes = [];
        foreach (GodotPropertyInfo property in cls.Properties)
        {
            if (!declared.TryGetValue(property.Name, out GodotPropertyInfo? becomes))
            {
                changes.Add(new TypingChange(cls.Name, property.Name, TypingChangeKind.PropertyRemoved, "", ""));
                continue;
            }

            changes.AddRange(ScalarChanges(cls.Name, property, becomes));
            if (EnumMemberChange(cls.Name, property, becomes, from, to) is { } members)
            {
                changes.Add(members);
            }
        }

        return changes;
    }

    // The property's own typing fields that are one value each.
    private static List<TypingChange> ScalarChanges(string className, GodotPropertyInfo was, GodotPropertyInfo becomes)
    {
        List<TypingChange> changes = [];
        AddIfChanged(changes, (className, was.Name), TypingChangeKind.Type, was.ValueType, becomes.ValueType);
        AddIfChanged(changes, (className, was.Name), TypingChangeKind.EnumType, was.EnumType ?? "", becomes.EnumType ?? "");
        AddIfChanged(changes, (className, was.Name), TypingChangeKind.Bitfield, Bool(was.Bitfield), Bool(becomes.Bitfield));
        AddIfChanged(changes, (className, was.Name), TypingChangeKind.Hint, Int(was.Hint), Int(becomes.Hint));
        AddIfChanged(changes, (className, was.Name), TypingChangeKind.HintString, was.HintString, becomes.HintString);
        AddIfChanged(changes, (className, was.Name), TypingChangeKind.Stored, Bool(was.IsStored), Bool(becomes.IsStored));
        return changes;
    }

    // One change for a field whose text differs between the versions.
    private static void AddIfChanged(
        List<TypingChange> changes,
        (string Class, string Property) owner,
        TypingChangeKind kind,
        string was,
        string becomes
    )
    {
        if (!string.Equals(was, becomes, StringComparison.Ordinal))
        {
            changes.Add(new TypingChange(owner.Class, owner.Property, kind, was, becomes));
        }
    }

    // The members of the enum both versions name, as the C# names an attribute's text uses.
    private static TypingChange? EnumMemberChange(
        string className,
        GodotPropertyInfo was,
        GodotPropertyInfo becomes,
        GodotMetadata from,
        GodotMetadata to
    )
    {
        if (was.EnumType is null || !string.Equals(was.EnumType, becomes.EnumType, StringComparison.Ordinal))
        {
            return null;
        }

        GodotEnumInfo? oldEnum = from.FindEnum(was.EnumType);
        GodotEnumInfo? newEnum = to.FindEnum(was.EnumType);
        if (oldEnum is null || newEnum is null)
        {
            return null;
        }

        FrozenDictionary<string, long> oldMembers = MemberValues(oldEnum);
        FrozenDictionary<string, long> newMembers = MemberValues(newEnum);
        if (oldMembers.Count == newMembers.Count && oldMembers.All(m => newMembers.TryGetValue(m.Key, out long value) && value == m.Value))
        {
            return null;
        }

        return new TypingChange(className, was.Name, TypingChangeKind.EnumMembers, MemberList(oldEnum), MemberDifferences(oldMembers, newMembers));
    }

    private static FrozenDictionary<string, long> MemberValues(GodotEnumInfo enumInfo)
    {
        Dictionary<string, long> values = new(StringComparer.Ordinal);
        foreach (GodotEnumMember member in enumInfo.Members)
        {
            values[member.CSharpName] = member.Value;
        }

        return values.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static string MemberList(GodotEnumInfo enumInfo) => string.Join(", ", enumInfo.Members.Select(m => $"{m.CSharpName}={Int(m.Value)}"));

    // The members that differ, as the C# name's change: renumbered or removed ones in old-name order, then added ones.
    private static string MemberDifferences(FrozenDictionary<string, long> was, FrozenDictionary<string, long> becomes)
    {
        List<string> differences = [];
        foreach (KeyValuePair<string, long> member in was.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            if (!becomes.TryGetValue(member.Key, out long value))
            {
                differences.Add($"{member.Key}: removed");
            }
            else if (value != member.Value)
            {
                differences.Add($"{member.Key}: {Int(member.Value)} -> {Int(value)}");
            }
        }

        differences.AddRange(becomes.Keys.Order(StringComparer.Ordinal).Where(name => !was.ContainsKey(name)).Select(name => $"{name}: added"));
        return string.Join("; ", differences);
    }

    // The C# names of the properties without a slash-named Godot name, which an element of unknown class reads by name.
    private static IEnumerable<string> CSharpNames(GodotMetadata metadata) =>
        metadata.Classes.SelectMany(c => c.Properties).Where(p => !p.Name.Contains('/', StringComparison.Ordinal)).Select(p => p.CSharpName);

    // A C# name that resolves to another form when the element's class is not known.
    private static IEnumerable<TypingChange> AgreementChanges(GodotMetadata from, GodotMetadata to)
    {
        XscnPropertyNames was = new(from);
        XscnPropertyNames becomes = new(to);
        foreach (string name in CSharpNames(from).Union(CSharpNames(to), StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            string oldForm = Agreement(was.Resolve(null, name));
            string newForm = Agreement(becomes.Resolve(null, name));
            if (!string.Equals(oldForm, newForm, StringComparison.Ordinal))
            {
                yield return new TypingChange("", name, TypingChangeKind.CSharpNameAgreement, oldForm, newForm);
            }
        }
    }

    // A resolved form as text: its Godot name, its Variant type, and whether the type is an enum or a bitfield.
    private static string Agreement(XscnResolvedProperty resolved)
    {
        string text = $"{resolved.GodotName}: {resolved.Type.VariantType ?? "unknown"}";
        return resolved.Type.Names switch
        {
            null => text,
            { IsFlags: true } => text + " (flags)",
            _ => text + " (enum)",
        };
    }

    private static string Bool(bool value) => value ? "True" : "False";

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Int(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static int Compare(TypingChange a, TypingChange b)
    {
        int byClass = string.CompareOrdinal(a.Class, b.Class);
        if (byClass != 0)
        {
            return byClass;
        }

        int byProperty = string.CompareOrdinal(a.Property, b.Property);
        return byProperty != 0 ? byProperty : a.Kind.CompareTo(b.Kind);
    }
}
