using GodotXaml.Core.Projects;

namespace GodotXaml.Core.Tests.Projects;

public sealed class GodotProjectVersionTests
{
    [Fact]
    public void ReadsTheVersionFromTheFeatures() =>
        Assert.Equal(
            "4.7",
            GodotProjectVersion.Read(
                """
                config_version=5

                [application]

                config/name="Demo"
                config/features=PackedStringArray("4.7", "C#", "Forward Plus")
                config/icon="res://icon.svg"
                """
            )
        );

    [Fact]
    public void ReadsTheVersionWhenItIsNotTheFirstFeature() =>
        Assert.Equal(
            "4.8",
            GodotProjectVersion.Read(
                """
                [application]
                config/features=PackedStringArray("C#", "4.8", "Mobile")
                """
            )
        );

    [Fact]
    public void GivesNullWithoutAnApplicationSection() =>
        Assert.Null(
            GodotProjectVersion.Read(
                """
                config_version=5

                [rendering]
                renderer/rendering_method="mobile"
                """
            )
        );

    [Fact]
    public void GivesNullWithoutTheFeaturesKey() =>
        Assert.Null(
            GodotProjectVersion.Read(
                """
                [application]
                config/name="Demo"
                """
            )
        );

    [Fact]
    public void GivesNullWithoutAVersionElement() =>
        Assert.Null(
            GodotProjectVersion.Read(
                """
                [application]
                config/features=PackedStringArray("C#", "Forward Plus", "4.7.2")
                """
            )
        );

    [Fact]
    public void IgnoresTheFeaturesKeyInAnotherSection() =>
        Assert.Null(
            GodotProjectVersion.Read(
                """
                [application]
                config/name="Demo"

                [other]
                config/features=PackedStringArray("4.7", "C#")
                """
            )
        );
}
