using System.Collections.Frozen;
using System.Globalization;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Xscn;

/// <summary>The type an attribute's value is read and written by.</summary>
/// <param name="VariantType">The Variant type name (<c>Vector2</c>, <c>StringName</c>), or null when the type is unknown.</param>
/// <param name="Names">The names of an integer's values, or null.</param>
public sealed record XscnValueType(string? VariantType, XscnEnumNames? Names)
{
    /// <summary>A value of unknown type, read and written as Godot value text.</summary>
    public static XscnValueType Unknown { get; } = new(null, null);
}

/// <summary>
/// Reads and writes an attribute's text as a value of its property's type: compact text for the types it has a form for,
/// Godot value text for every other type and for an unknown one. Reading never throws on bad text; it gives an
/// <see cref="XscnInvalid"/> saying what was expected.
/// </summary>
public static class XscnValueText
{
    private const string NullText = "{x:Null}";
    private const string ResourcePrefix = "res://";

    // variant_parser.cpp:150-160, the names Godot's float text uses for values that are not finite.
    private static readonly FrozenDictionary<string, double> NonFinite = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["inf"] = double.PositiveInfinity,
        ["-inf"] = double.NegativeInfinity,
        ["inf_neg"] = double.NegativeInfinity,
        ["nan"] = double.NaN,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // The types with a compact form; a writer gives null for a value its form cannot hold, which is then Godot value text.
    private static readonly FrozenDictionary<string, Codec> Codecs = new Dictionary<string, Codec>(StringComparer.Ordinal)
    {
        ["bool"] = new(ReadBool, WriteBool),
        ["int"] = new(ReadInt, WriteInt),
        ["float"] = new(ReadFloat, WriteFloat),
        ["String"] = new((text, _) => new XscnScalar(new GodotString(text)), WriteLiteral),
        ["StringName"] = new((text, _) => new XscnScalar(new GodotStringName(text)), WriteLiteral),
        ["NodePath"] = new((text, _) => new XscnScalar(new GodotNodePath(text)), WriteNodePath),
        ["Vector2"] = Reals(GodotRealKind.Vector2),
        ["Vector3"] = Reals(GodotRealKind.Vector3),
        ["Vector4"] = Reals(GodotRealKind.Vector4),
        ["Color"] = Reals(GodotRealKind.Color),
        ["Vector2i"] = Ints(GodotIntKind.Vector2I),
        ["Vector3i"] = Ints(GodotIntKind.Vector3I),
        ["Vector4i"] = Ints(GodotIntKind.Vector4I),
        ["Object"] = new(ReadObject, WriteObject),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The type a property's attribute is read and written by.</summary>
    /// <param name="metadata">The metadata the property comes from.</param>
    /// <param name="property">The property.</param>
    /// <returns>
    /// The property's type, the default's kind winning when String and StringName disagree; unknown for a Variant (<c>Nil</c>) property.
    /// </returns>
    public static XscnValueType TypeOf(GodotMetadata metadata, GodotPropertyInfo property)
    {
        ArgumentNullException.ThrowIfNull(property);
        string type = (property.Type, property.Default) switch
        {
            ("String", GodotStringName) => "StringName",
            ("StringName", GodotString) => "String",
            _ => property.Type,
        };
        return type == "Nil" ? XscnValueType.Unknown : new XscnValueType(type, XscnEnumText.For(metadata, property));
    }

    /// <summary>The type of a theme override item's attribute.</summary>
    /// <param name="item">The item.</param>
    /// <returns>The item's type.</returns>
    public static XscnValueType TypeOf(GodotThemeOverrideInfo item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new XscnValueType(item.Type, null);
    }

    /// <summary>The value an attribute's text gives.</summary>
    /// <param name="text">The attribute's text, its XML references already decoded.</param>
    /// <param name="type">The attribute's type.</param>
    /// <returns>The value, or an <see cref="XscnInvalid"/> holding the text and what was expected.</returns>
    public static XscnValue Read(string text, XscnValueType type)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(type);
        if (CodecOf(type) is { } codec)
        {
            return codec.Read(text, type);
        }

        return type.VariantType is null ? ReadUntyped(text) : ReadGodotText(text);
    }

    /// <summary>How a value is written: as the attribute's text when that text reads back as the same value, else as <c>x:Prop</c>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="type">The attribute's type.</param>
    /// <returns>
    /// The attribute text (the compact form, or Godot value text for a type without one; an invalid value's own text), or,
    /// for a value the attribute's text cannot hold (an int on a String property), that the property needs <c>x:Prop</c>.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is an <see cref="XscnInline"/>, which is written as an element.</exception>
    public static XscnWriteResult Write(XscnValue value, XscnValueType type)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(type);
        if (value is XscnInvalid invalid)
        {
            return new XscnAttributeText(invalid.Text);
        }

        string text = WriteText(value, type);
        return Read(text, type).Equals(value) ? new XscnAttributeText(text) : new XscnNeedsProp(WriteText(value, XscnValueType.Unknown));
    }

    private static string WriteText(XscnValue value, XscnValueType type) =>
        value switch
        {
            XscnScalar scalar => CodecOf(type)?.Write(scalar.Value, type) ?? GodotText(scalar.Value),
            XscnFile file => WriteFile(file, type),
            XscnKeyRef key => "{" + key.Key + "}",
            _ => throw new ArgumentException("An inline resource is written as a property element, not as attribute text.", nameof(value)),
        };

    // Untyped Godot value text, where a file alone, Resource("res://…"), is a file, and {Key} is a key reference: braces
    // around one name with no colon, which no Godot dictionary is.
    private static XscnValue ReadUntyped(string text)
    {
        if (IsKeyRef(text))
        {
            return new XscnKeyRef(text[1..^1]);
        }

        XscnValue value = ReadGodotText(text);
        return value is XscnScalar { Value: GodotPathRef { Uid: null, Path: { } path } } && path.StartsWith(ResourcePrefix, StringComparison.Ordinal)
            ? new XscnFile(path)
            : value;
    }

    private static Codec? CodecOf(XscnValueType type) => type.VariantType is null ? null : Codecs.GetValueOrDefault(type.VariantType);

    private static string GodotText(GodotValue value) => GodotValueWriter.Write(value, ValueWriteOptions.Default);

    private static bool IsObject(XscnValueType type) => type.VariantType == "Object";

    private static string WriteFile(XscnFile file, XscnValueType type) => IsObject(type) ? file.Path : GodotText(new GodotPathRef(null, file.Path));

    private static string Itos(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static XscnInvalid Invalid(string text, string message) => new(text, message);

    private static XscnValue ReadBool(string text, XscnValueType type)
    {
        bool isTrue = string.Equals(text, "True", StringComparison.OrdinalIgnoreCase);
        bool isFalse = string.Equals(text, "False", StringComparison.OrdinalIgnoreCase);
        return isTrue || isFalse ? new XscnScalar(new GodotBool(isTrue)) : Invalid(text, "Expected True or False.");
    }

    private static string? WriteBool(GodotValue value, XscnValueType type) => value is GodotBool flag ? (flag.Value ? "True" : "False") : null;

    private static XscnValue ReadInt(string text, XscnValueType type)
    {
        long? value = type.Names is null ? ParseLong(text) : type.Names.Read(text);
        return value is null ? Invalid(text, ExpectedInt(type.Names)) : new XscnScalar(new GodotInt(value.Value));
    }

    private static string ExpectedInt(XscnEnumNames? names)
    {
        if (names is null || names.Names.IsEmpty)
        {
            return "Expected an integer.";
        }

        string joined = names.IsFlags ? ", or several of them joined with commas," : "";
        return $"Expected one of {string.Join(", ", names.Names.Select(n => n.Name))}{joined} or an integer.";
    }

    private static string? WriteInt(GodotValue value, XscnValueType type) =>
        value is GodotInt number ? type.Names?.Write(number.Value) ?? Itos(number.Value) : null;

    private static XscnValue ReadFloat(string text, XscnValueType type) =>
        ParseReal(text) is { } value ? new XscnScalar(new GodotFloat(value)) : Invalid(text, "Expected a number.");

    private static string? WriteFloat(GodotValue value, XscnValueType type) =>
        value switch
        {
            GodotFloat number => GodotFloatFormat.Format(number.Value, compat: true),
            _ => null,
        };

    private static string? WriteLiteral(GodotValue value, XscnValueType type) =>
        value switch
        {
            GodotString text => text.Value,
            GodotStringName name => name.Value,
            _ => null,
        };

    private static string? WriteNodePath(GodotValue value, XscnValueType type) => (value as GodotNodePath)?.Path;

    private static Codec Reals(GodotRealKind kind) =>
        new((text, _) => ReadReals(text, kind), (value, _) => value is GodotReal real && real.Kind == kind ? JoinReals(real) : null);

    private static Codec Ints(GodotIntKind kind) =>
        new((text, _) => ReadInts(text, kind), (value, _) => value is GodotInts ints && ints.Kind == kind ? JoinInts(ints) : null);

    private static XscnValue ReadReals(string text, GodotRealKind kind)
    {
        int arity = GodotValueTables.Real(kind).Arity;
        List<double?> components = [.. text.Split(',').Select(ParseReal)];
        return components.Count == arity && components.All(c => c is not null)
            ? new XscnScalar(new GodotReal(kind, [.. components.Select(c => (float)c!.Value)]))
            : Invalid(text, ExpectedComponents(arity, "numbers"));
    }

    private static XscnValue ReadInts(string text, GodotIntKind kind)
    {
        int arity = GodotValueTables.Int(kind).Arity;
        List<long?> components = [.. text.Split(',').Select(ParseLong)];
        return components.Count == arity && components.All(c => c is >= int.MinValue and <= int.MaxValue)
            ? new XscnScalar(new GodotInts(kind, [.. components.Select(c => (int)c!.Value)]))
            : Invalid(text, ExpectedComponents(arity, "integers"));
    }

    private static string ExpectedComponents(int arity, string what) => $"Expected {arity} {what} separated by commas.";

    private static string JoinReals(GodotReal real) => string.Join(", ", real.Components.Select(c => GodotFloatFormat.Format(c, compat: true)));

    private static string JoinInts(GodotInts ints) => string.Join(", ", ints.Components.Select(c => Itos(c)));

    private static double? ParseReal(string text)
    {
        string trimmed = text.Trim();
        if (NonFinite.TryGetValue(trimmed, out double named))
        {
            return named;
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
    }

    private static long? ParseLong(string text) =>
        long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value) ? value : null;

    private static XscnValue ReadObject(string text, XscnValueType type)
    {
        if (text == NullText)
        {
            return new XscnScalar(GodotNull.Instance);
        }

        if (text.StartsWith(ResourcePrefix, StringComparison.Ordinal))
        {
            return new XscnFile(text);
        }

        return IsKeyRef(text) ? new XscnKeyRef(text[1..^1]) : Invalid(text, "Expected a res:// path, {Key} or {x:Null}.");
    }

    private static string? WriteObject(GodotValue value, XscnValueType type) => value is GodotNull ? NullText : null;

    // {Key}: a key of at least one character, without whitespace or braces.
    private static bool IsKeyRef(string text) =>
        text.Length > 2 && text[0] == '{' && text[^1] == '}' && !text[1..^1].Any(c => char.IsWhiteSpace(c) || c is '{' or '}' or ':' or ',' or '"');

    private static XscnValue ReadGodotText(string text)
    {
        GodotValue value;
        try
        {
            value = GodotValueReader.Read(text);
        }
        catch (GodotValueParseException error)
        {
            return Invalid(text, $"Expected Godot value text: {error.Message}");
        }

        return GodotReferences.HasExtResource(value)
            ? Invalid(text, "ExtResource(…) is not used in .xscn; refer to a file as Resource(\"res://…\").")
            : new XscnScalar(value);
    }

    private sealed record Codec(Func<string, XscnValueType, XscnValue> Read, Func<GodotValue, XscnValueType, string?> Write);
}

/// <summary>Finds the external resource references inside a value.</summary>
internal static class GodotReferences
{
    /// <summary>Whether a value holds an <c>ExtResource(…)</c> anywhere, a container's type included.</summary>
    /// <param name="value">The value.</param>
    /// <returns>Whether it does.</returns>
    public static bool HasExtResource(GodotValue value) =>
        value switch
        {
            GodotResourceRef reference => reference.Kind == GodotRefKind.External,
            GodotArray array => InType(array.ElementType) || array.Items.Any(HasExtResource),
            GodotDictionary dictionary => InDictionary(dictionary),
            GodotObject obj => obj.Properties.Any(p => HasExtResource(p.Value)),
            _ => false,
        };

    private static bool InDictionary(GodotDictionary dictionary) =>
        InType(dictionary.KeyType) || InType(dictionary.ValueType) || dictionary.Entries.Any(e => HasExtResource(e.Key) || HasExtResource(e.Value));

    private static bool InType(GodotTypeRef? type) => type?.Script is { } script && HasExtResource(script);
}

/// <summary>How <see cref="XscnValueText.Write"/> writes a value.</summary>
public abstract record XscnWriteResult
{
    private protected XscnWriteResult() { }
}

/// <summary>The value is the attribute's text, which reads back as the same value.</summary>
/// <param name="Text">The text, before XML escaping.</param>
public sealed record XscnAttributeText(string Text) : XscnWriteResult;

/// <summary>
/// The attribute's text cannot hold the value, so the property is written as <c>&lt;x:Prop Name="…" Value="…" /&gt;</c>, whose
/// value is untyped Godot value text, read with <see cref="XscnValueType.Unknown"/>.
/// </summary>
/// <param name="GodotText">The value as Godot value text.</param>
public sealed record XscnNeedsProp(string GodotText) : XscnWriteResult;
