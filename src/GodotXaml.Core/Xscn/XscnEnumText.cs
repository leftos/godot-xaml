using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using GodotXaml.Core.Metadata;

namespace GodotXaml.Core.Xscn;

/// <summary>One name an integer property's value can be written as.</summary>
/// <param name="Name">The name as an attribute value holds it (<c>Ignore</c>, <c>FullRect</c>).</param>
/// <param name="Value">The integer it stands for.</param>
public sealed record XscnEnumName(string Name, long Value);

/// <summary>
/// The names of an integer property's values, and how an attribute writes and reads them: one name for an enum, .NET
/// <c>[Flags]</c> text for a bitfield, the integer when no name fits.
/// </summary>
public sealed class XscnEnumNames
{
    internal XscnEnumNames(ImmutableArray<XscnEnumName> names, bool isFlags)
    {
        Names = names;
        IsFlags = isFlags;
    }

    /// <summary>The names, in declaration order; empty when every value is written as its integer.</summary>
    public ImmutableArray<XscnEnumName> Names { get; }

    /// <summary>Whether a value combines names as bits.</summary>
    public bool IsFlags { get; }

    /// <summary>The attribute text of a value.</summary>
    /// <param name="value">The integer.</param>
    /// <returns>Its name, its names joined with <c>", "</c> for flags, or the integer when no names make it up.</returns>
    public string Write(long value) => IsFlags ? WriteFlags(value) : NameOf(value) ?? Itos(value);

    /// <summary>The value an attribute's text gives.</summary>
    /// <param name="text">A name or an integer; for flags, any number of them joined with <c>,</c>.</param>
    /// <returns>The integer, or null when the text is not a name of this property or an integer.</returns>
    public long? Read(string text) => IsFlags ? ReadFlags(text) : ReadPart(text);

    private static string Itos(long value) => value.ToString(CultureInfo.InvariantCulture);

    private string? NameOf(long value) => Names.FirstOrDefault(n => n.Value == value)?.Name;

    // .NET's Enum.ToString for a [Flags] enum: the largest names whose bits are all set are taken first, then written in
    // ascending order; bits no name covers mean the integer, and 0 is the zero name when there is one.
    private string WriteFlags(long value)
    {
        if (value == 0)
        {
            return NameOf(0) ?? "0";
        }

        long remaining = value;
        var taken = new List<string>();
        foreach (XscnEnumName name in Names.Where(n => n.Value != 0).OrderByDescending(n => n.Value))
        {
            if ((remaining & name.Value) == name.Value)
            {
                remaining &= ~name.Value;
                taken.Add(name.Name);
            }
        }

        taken.Reverse();
        return remaining == 0 ? string.Join(", ", taken) : Itos(value);
    }

    private long? ReadFlags(string text)
    {
        long value = 0;
        foreach (string part in text.Split(','))
        {
            long? bits = ReadPart(part);
            if (bits is null)
            {
                return null;
            }

            value |= bits.Value;
        }

        return value;
    }

    private long? ReadPart(string text)
    {
        string trimmed = text.Trim();
        if (long.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value))
        {
            return value;
        }

        return Names.FirstOrDefault(n => n.Name == trimmed)?.Value;
    }
}

/// <summary>Finds the names of an integer property's values: its enum's C# member names, or its hint's display names.</summary>
public static class XscnEnumText
{
    // core/object/object.h, PropertyHint: PROPERTY_HINT_ENUM = 2, PROPERTY_HINT_FLAGS = 6.
    private const int HintEnum = 2;
    private const int HintFlags = 6;

    /// <summary>The names of a property's values.</summary>
    /// <param name="metadata">The metadata the property comes from.</param>
    /// <param name="property">The property.</param>
    /// <returns>
    /// The enum's members by C# name; else the ENUM or FLAGS hint's display names in PascalCase (none for an empty hint);
    /// null for a property that is not an int with an enum or one of those hints.
    /// </returns>
    public static XscnEnumNames? For(GodotMetadata metadata, GodotPropertyInfo property)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(property);
        if (property.Type != "int")
        {
            return null;
        }

        GodotEnumInfo? enumInfo = property.EnumType is null ? null : metadata.FindEnum(property.EnumType);
        if (enumInfo is not null)
        {
            return new XscnEnumNames([.. enumInfo.Members.Select(m => new XscnEnumName(m.CSharpName, m.Value))], property.Bitfield);
        }

        return property.Hint switch
        {
            HintEnum => new XscnEnumNames(EnumHintNames(property.HintString), isFlags: false),
            HintFlags => new XscnEnumNames(FlagsHintNames(property.HintString), isFlags: true),
            _ => null,
        };
    }

    /// <summary>A hint's display name in PascalCase: split on every character that is not a letter or digit, each word capitalised.</summary>
    /// <param name="displayName">The display name (<c>Bilinear (Left and Right)</c>).</param>
    /// <returns>The name (<c>BilinearLeftAndRight</c>); empty when the display name has no letter or digit.</returns>
    public static string PascalCase(string displayName)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        var text = new StringBuilder(displayName.Length);
        bool wordStart = true;
        foreach (char c in displayName)
        {
            bool inWord = char.IsLetterOrDigit(c);
            if (inWord)
            {
                text.Append(wordStart ? char.ToUpperInvariant(c) : c);
            }

            wordStart = !inWord;
        }

        return text.ToString();
    }

    // EditorPropertyEnum::setup (editor/inspector/editor_properties.cpp:972-988): an option without ":value" is one more
    // than the option before it, the first 0.
    private static ImmutableArray<XscnEnumName> EnumHintNames(string hintString)
    {
        ImmutableArray<XscnEnumName>.Builder names = ImmutableArray.CreateBuilder<XscnEnumName>();
        long current = 0;
        foreach (string option in Options(hintString))
        {
            string[] parts = option.Split(':');
            current = parts.Length > 1 ? ToInt(parts[1]) : current;
            Add(names, parts[0], current);
            current++;
        }

        return names.ToImmutable();
    }

    // EditorPropertyFlags::setup (editor_properties.cpp:1039-1063): option i is bit 1u << i (C# masks the shift to five
    // bits, as x86 does, so a 33rd option is bit 0 again) unless it gives ":value", cast to uint32; an empty option and
    // an explicit 0 are skipped, and both still count.
    private static ImmutableArray<XscnEnumName> FlagsHintNames(string hintString)
    {
        ImmutableArray<XscnEnumName>.Builder names = ImmutableArray.CreateBuilder<XscnEnumName>();
        string[] options = Options(hintString);
        for (int i = 0; i < options.Length; i++)
        {
            string[] parts = options[i].Trim().Split(':');
            uint value = parts.Length > 1 ? unchecked((uint)ToInt(parts[1])) : 1u << i;
            if (value != 0)
            {
                Add(names, parts[0], value);
            }
        }

        return names.ToImmutable();
    }

    private static string[] Options(string hintString) => hintString.Length == 0 ? [] : hintString.Split(',');

    // A display name whose PascalCase form is empty, all digits (it would read as an integer) or already taken has no
    // name, so its value is written as the integer.
    private static void Add(ImmutableArray<XscnEnumName>.Builder names, string displayName, long value)
    {
        string name = PascalCase(displayName);
        if (!name.All(char.IsAsciiDigit) && !names.Any(n => n.Name == name))
        {
            names.Add(new XscnEnumName(name, value));
        }
    }

    // String::to_int and _to_int (core/string/ustring.cpp:2264-2308): only the text before the first '.' counts; every
    // character but a digit is skipped, a '-' before the first non-zero digit flips the sign, and a value past the 64-bit
    // range is clamped to it.
    private static long ToInt(string text)
    {
        int dot = text.IndexOf('.', StringComparison.Ordinal);
        ulong integer = 0;
        bool positive = true;
        foreach (char c in dot >= 0 ? text[..dot] : text)
        {
            if (char.IsAsciiDigit(c))
            {
                if (Overflows(integer, c, positive))
                {
                    return Signed(positive ? long.MaxValue : 1UL << 63, positive);
                }

                integer = (integer * 10) + (ulong)(c - '0');
            }
            else
            {
                positive ^= integer == 0 && c == '-';
            }
        }

        return Signed(integer, positive);
    }

    private static long Signed(ulong integer, bool positive) => positive ? (long)integer : unchecked((long)(0 - integer));

    // _to_int's overflow check, made once a value has more than 18 digits; making it for every digit gives the same answer.
    private static bool Overflows(ulong integer, char digit, bool positive)
    {
        const ulong limit = long.MaxValue / 10;
        return integer > limit || (integer == limit && digit > (positive ? '7' : '8'));
    }
}
