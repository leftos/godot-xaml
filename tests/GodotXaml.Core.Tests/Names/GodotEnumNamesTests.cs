using GodotXaml.Core.Names;

namespace GodotXaml.Core.Tests.Names;

// One case per rule of bindings_generator.cpp's _determine_enum_prefix and _apply_prefix_to_enum_constants, with the
// members as metadata/godot-4.7.json lists them.
public sealed class GodotEnumNamesTests
{
    [Fact]
    public void StripsTheSharedPrefix()
    {
        string[] names = ["MOUSE_FILTER_STOP", "MOUSE_FILTER_PASS", "MOUSE_FILTER_IGNORE"];

        Assert.Equal(["Stop", "Pass", "Ignore"], GodotEnumNames.ClassEnumMembers(names));
    }

    [Fact]
    public void StripsOnlyTheWordsEveryMemberShares()
    {
        string[] names = ["SIZE_SHRINK_BEGIN", "SIZE_FILL", "SIZE_EXPAND", "SIZE_EXPAND_FILL", "SIZE_SHRINK_CENTER", "SIZE_SHRINK_END"];

        Assert.Equal(["ShrinkBegin", "Fill", "Expand", "ExpandFill", "ShrinkCenter", "ShrinkEnd"], GodotEnumNames.ClassEnumMembers(names));
    }

    [Fact]
    public void KeepsOneMoreWordWhenTheStrippedNameWouldStartWithADigit()
    {
        string[] names = ["KEY_NONE", "KEY_SPECIAL", "KEY_0", "KEY_A"];

        Assert.Equal(["None", "Special", "Key0", "A"], GodotEnumNames.GlobalEnumMembers("Key", names));
    }

    [Fact]
    public void TreatsFlagAndFlagsAsTheSameLastPrefixWord()
    {
        string[] names = ["METHOD_FLAG_NORMAL", "METHOD_FLAG_EDITOR", "METHOD_FLAGS_DEFAULT"];

        Assert.Equal(["Normal", "Editor", "Default"], GodotEnumNames.GlobalEnumMembers("MethodFlags", names));
    }

    [Fact]
    public void StripsErrFromTheGlobalErrorEnum()
    {
        string[] names = ["OK", "FAILED", "ERR_UNAVAILABLE"];

        Assert.Equal(["Ok", "Failed", "Unavailable"], GodotEnumNames.GlobalEnumMembers("Error", names));
    }

    [Fact]
    public void ForcesTheErrPrefixOnlyOnTheGlobalErrorEnum()
    {
        string[] names = ["OK", "FAILED", "ERR_UNAVAILABLE"];

        Assert.Equal(["Ok", "Failed", "ErrUnavailable"], GodotEnumNames.GlobalEnumMembers("Side", names));
        Assert.Equal(["Ok", "Failed", "ErrUnavailable"], GodotEnumNames.ClassEnumMembers(names));
    }

    [Fact]
    public void StripsAllButTheLastWordOfASingleMember()
    {
        Assert.Equal(["One"], GodotEnumNames.ClassEnumMembers(["ONLY_ONE"]));
        Assert.Equal(["Single"], GodotEnumNames.ClassEnumMembers(["SINGLE"]));
    }

    [Fact]
    public void KeepsWholeNamesThatShareNoPrefix()
    {
        string[] names = ["LEFT_TOP", "RIGHT_BOTTOM"];

        Assert.Equal(["LeftTop", "RightBottom"], GodotEnumNames.ClassEnumMembers(names));
    }

    [Fact]
    public void NamesAnEmptyEnumWithNoMembers() => Assert.Empty(GodotEnumNames.ClassEnumMembers([]));
}
