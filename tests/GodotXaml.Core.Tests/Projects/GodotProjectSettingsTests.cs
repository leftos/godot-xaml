using GodotXaml.Core.Projects;

namespace GodotXaml.Core.Tests.Projects;

public sealed class GodotProjectSettingsTests
{
    [Fact]
    public void UsesTheHiddenDataFolderWithoutTheKey() =>
        Assert.True(
            GodotProjectSettings.UsesHiddenDataFolder(
                """
                config_version=5

                [application]

                config/name="Demo"
                """
            )
        );

    [Fact]
    public void UsesTheVisibleDataFolderForTheLiteralFalse() =>
        Assert.False(GodotProjectSettings.UsesHiddenDataFolder(Application("config/use_hidden_project_data_directory=false")));

    [Fact]
    public void UsesTheHiddenDataFolderForTheLiteralTrue() =>
        Assert.True(GodotProjectSettings.UsesHiddenDataFolder(Application("config/use_hidden_project_data_directory=true")));

    [Fact]
    public void ReadsTheValueWithSpacesAroundTheSeparator() =>
        Assert.False(GodotProjectSettings.UsesHiddenDataFolder(Application("config/use_hidden_project_data_directory = false")));

    [Fact]
    public void IgnoresTheKeyInAnotherSection() =>
        Assert.True(
            GodotProjectSettings.UsesHiddenDataFolder(
                """
                config_version=5

                [application]

                config/name="Demo"

                [rendering]

                config/use_hidden_project_data_directory=false
                """
            )
        );

    [Fact]
    public void IgnoresTheKeyInAComment() =>
        Assert.True(
            GodotProjectSettings.UsesHiddenDataFolder(
                """
                config_version=5

                [application]

                ;config/use_hidden_project_data_directory=false
                """
            )
        );

    [Fact]
    public void KeepsTheHiddenDataFolderForAnotherSpellingOfFalse() =>
        Assert.True(GodotProjectSettings.UsesHiddenDataFolder(Application("config/use_hidden_project_data_directory=False")));

    [Fact]
    public void ReadsFalseBeforeAComment() =>
        Assert.False(GodotProjectSettings.UsesHiddenDataFolder(Application("config/use_hidden_project_data_directory=false ; keep visible")));

    [Fact]
    public void ReadsTheKeyAfterACommentedHeader() =>
        Assert.False(
            GodotProjectSettings.UsesHiddenDataFolder("config_version=5\n\n[application] ; note\n\nconfig/use_hidden_project_data_directory=false\n")
        );

    [Fact]
    public void ReadsTheKeyAfterASpacedHeader() =>
        Assert.False(
            GodotProjectSettings.UsesHiddenDataFolder("config_version=5\n\n[ application ]\n\nconfig/use_hidden_project_data_directory=false\n")
        );

    [Fact]
    public void SkipsAHeaderInsideAMultiLineString() =>
        Assert.False(
            GodotProjectSettings.UsesHiddenDataFolder(
                "config_version=5\n\n[application]\n\nconfig/description=\"line one\n[Beta] line two\"\n"
                    + "config/use_hidden_project_data_directory=false\n"
            )
        );

    [Fact]
    public void LetsALaterTrueOverrideAnEarlierFalse() =>
        Assert.True(
            GodotProjectSettings.UsesHiddenDataFolder(
                Application("config/use_hidden_project_data_directory=false\nconfig/use_hidden_project_data_directory=true")
            )
        );

    [Fact]
    public void LetsALaterFalseOverrideAnEarlierTrue() =>
        Assert.False(
            GodotProjectSettings.UsesHiddenDataFolder(
                Application("config/use_hidden_project_data_directory=true\nconfig/use_hidden_project_data_directory=false")
            )
        );

    [Fact]
    public void IgnoresALongerKey() =>
        Assert.True(GodotProjectSettings.UsesHiddenDataFolder(Application("config/use_hidden_project_data_directory.windows=false")));

    [Fact]
    public void ReadsFalseAcrossCRLFLineEnds() =>
        Assert.False(
            GodotProjectSettings.UsesHiddenDataFolder(
                "config_version=5\r\n\r\n[application]\r\n\r\nconfig/use_hidden_project_data_directory=false\r\n"
            )
        );

    private static string Application(string line) => $"config_version=5\n\n[application]\n\nconfig/name=\"Demo\"\n{line}\n";
}
