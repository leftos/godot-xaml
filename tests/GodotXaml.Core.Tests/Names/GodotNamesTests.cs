using GodotXaml.Core.Names;

namespace GodotXaml.Core.Tests.Names;

public sealed class GodotNamesTests
{
    [Theory]
    [InlineData("mouse_filter", false, "MouseFilter")]
    [InlineData("size_flags_horizontal", false, "SizeFlagsHorizontal")]
    [InlineData("custom_minimum_size", false, "CustomMinimumSize")]
    [InlineData("position", false, "Position")]
    // naming_utils.cpp:59-86, one row per part override; the lookup uppercases a part that is not already upper case.
    [InlineData("aa", false, "AA")]
    [InlineData("ao", false, "AO")]
    [InlineData("filename", false, "FileName")]
    [InlineData("fadein", false, "FadeIn")]
    [InlineData("fadeout", false, "FadeOut")]
    [InlineData("fx", false, "FX")]
    [InlineData("gi", false, "GI")]
    [InlineData("gzip", false, "GZip")]
    [InlineData("hbox", false, "HBox")]
    [InlineData("id", false, "Id")]
    [InlineData("io", false, "IO")]
    [InlineData("ip", false, "IP")]
    [InlineData("iv", false, "IV")]
    [InlineData("macos", false, "MacOS")]
    [InlineData("nodepath", false, "NodePath")]
    [InlineData("spirv", false, "SpirV")]
    [InlineData("stdin", false, "StdIn")]
    [InlineData("stdout", false, "StdOut")]
    [InlineData("username", false, "UserName")]
    [InlineData("uv", false, "UV")]
    [InlineData("uv2", false, "UV2")]
    [InlineData("vbox", false, "VBox")]
    [InlineData("whitespace", false, "WhiteSpace")]
    [InlineData("wm", false, "WM")]
    [InlineData("xr", false, "XR")]
    [InlineData("xrapi", false, "XRApi")]
    // naming_utils.cpp:196-240, the cases the conversion handles specially.
    [InlineData("_position", false, "_Position")]
    [InlineData("position_", false, "Position_")]
    [InlineData("__a", false, "__A")]
    [InlineData("a__b", false, "A__B")]
    [InlineData("", false, "_")]
    [InlineData("MOUSE_FILTER", false, "MOUSEFILTER")]
    [InlineData("MOUSE_FILTER", true, "MouseFilter")]
    [InlineData("MOUSE_ID", true, "MouseId")]
    [InlineData("VECTOR2_ID", true, "Vector2Id")]
    [InlineData("node_2d", false, "Node2D")]
    [InlineData("vector2d", false, "Vector2D")]
    // ucaps.h's tables, not .NET's casing: _find_upper(U+0131) is U+0049, which the invariant culture leaves alone.
    [InlineData("ı_x", false, "IX")]
    // Godot's String holds code points, so a letter beyond U+FFFF is cased as one character.
    [InlineData("\U00010428b_c", false, "\U00010400bC")]
    [InlineData("A\U00010400", true, "A\U00010428")]
    public void SnakeToPascalCaseReturnsTheConvertedName(string name, bool inputIsUpper, string expected) =>
        Assert.Equal(expected, GodotNames.SnakeToPascalCase(name, inputIsUpper));

    [Theory]
    [InlineData("Node2D", "Node2D")]
    [InlineData("Label", "Label")]
    // naming_utils.cpp:45-56, one row per identifier override.
    [InlineData("BitMap", "Bitmap")]
    [InlineData("JSONRPC", "JsonRpc")]
    [InlineData("Object", "GodotObject")]
    [InlineData("OpenXRIPBinding", "OpenXRIPBinding")]
    [InlineData("SkeletonModification2DCCDIK", "SkeletonModification2DCcdik")]
    [InlineData("SkeletonModification2DFABRIK", "SkeletonModification2DFabrik")]
    [InlineData("SkeletonModification3DCCDIK", "SkeletonModification3DCcdik")]
    [InlineData("SkeletonModification3DFABRIK", "SkeletonModification3DFabrik")]
    [InlineData("System", "System_")]
    [InlineData("Thread", "GodotThread")]
    // naming_utils.cpp:59-86, one row per part override, as a part an all-upper identifier splits into.
    [InlineData("FILENAME", "FileName")]
    [InlineData("FADEIN", "FadeIn")]
    [InlineData("FADEOUT", "FadeOut")]
    [InlineData("GZIP", "GZip")]
    [InlineData("HBOX", "HBox")]
    [InlineData("MACOS", "MacOS")]
    [InlineData("NODEPATH", "NodePath")]
    [InlineData("SPIRV", "SpirV")]
    [InlineData("STDIN", "StdIn")]
    [InlineData("STDOUT", "StdOut")]
    [InlineData("USERNAME", "UserName")]
    [InlineData("VBOX", "VBox")]
    [InlineData("WHITESPACE", "WhiteSpace")]
    [InlineData("XRAPI", "XRApi")]
    // The part overrides of one or two letters, which only the splitter's boundaries can produce: a capital run followed
    // by a lower-case letter hands its last capital to the next word.
    [InlineData("AAButton", "AAButton")]
    [InlineData("AOButton", "AOButton")]
    [InlineData("FXButton", "FXButton")]
    [InlineData("GIButton", "GIButton")]
    [InlineData("IDCard", "IdCard")]
    [InlineData("IODevice", "IODevice")]
    [InlineData("IPAddress", "IPAddress")]
    [InlineData("IVStore", "IVStore")]
    [InlineData("UVMap", "UVMap")]
    [InlineData("MainWM", "MainWM")]
    [InlineData("XRController", "XRController")]
    // The UV2 override is never reached: the splitter starts a part at every digit, so no part is ever UV2; this row pins
    // what UV2 itself becomes.
    [InlineData("UV2", "UV2")]
    // naming_utils.cpp:145-194, the cases the conversion handles specially.
    [InlineData("CPUParticles3D", "CpuParticles3D")]
    [InlineData("_Foo", "_Foo")]
    [InlineData("Node2d", "Node2D")]
    [InlineData("Id", "ID")]
    [InlineData("", "")]
    // Lengths and split points count code points (naming_utils.cpp:150-151, :105-139), never splitting a surrogate pair.
    [InlineData("\U00010428a", "\U00010400A")]
    [InlineData("A\U00010428b", "A\U00010400b")]
    public void PascalToPascalCaseReturnsTheConvertedName(string name, string expected) =>
        Assert.Equal(expected, GodotNames.PascalToPascalCase(name));

    // The unpaired-surrogate rows are facts: an attribute's strings are stored as UTF-8, which cannot hold a lone
    // surrogate. Godot keeps an unpaired surrogate as its own code point (ustring.cpp:2098-2121).
    [Fact]
    public void PascalToPascalCaseCountsAnUnpairedSurrogateAsOneCodePoint() =>
        Assert.Equal("\uD800\U00010400", GodotNames.PascalToPascalCase("\uD800\U00010428"));

    [Fact]
    public void SnakeToPascalCaseKeepsAnUnpairedSurrogate() => Assert.Equal("\uD800x", GodotNames.SnakeToPascalCase("\uD800x", false));
}
