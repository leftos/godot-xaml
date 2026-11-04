using System.Collections.Immutable;
using GodotXaml.Core.Conversion;

namespace GodotXaml.Core.Tests.Conversion;

// The local uniforms of a shader's text, in the order ShaderMaterial::_get_property_list lists them, or null for a shader
// whose uniforms only the preprocessor could tell.
public sealed class ShaderUniformsTests
{
    // Each case's expected names, space-separated, in the order Godot lists them.
    [Theory]
    [InlineData("shader_type spatial;\nuniform float b;\nuniform vec4 a;\nuniform sampler2D c;", "b a c")]
    [InlineData("uniform float b[2];\nuniform float[3] a;\nuniform highp vec2 c[4];", "b a c")]
    [InlineData(
        "uniform float b : hint_range(0.0, 1.0) = 0.5;\nuniform vec4 a : source_color = vec4(1.0);\n"
            + "uniform float c[2] = {1.0, 2.0};\nuniform sampler2D d : filter_linear, repeat_enable;",
        "b a c d"
    )]
    [InlineData("// uniform float x;\n/** The b value. */\nuniform float b;\n/* uniform float y; */ uniform vec4 a; // uniform float z;", "b a")]
    [InlineData("uniform float b;\nvoid fragment() {\n  uniform float x;\n  if (true) { ALBEDO = vec3(b); }\n}\nuniform vec4 a;", "b a")]
    [InlineData("instance uniform float x;\nuniform float b;\nglobal uniform vec4 y;\nuniform vec4 a;", "b a")]
    [InlineData("uniform float a;\ngroup_uniforms g;\nuniform float c;\ngroup_uniforms;\nuniform float d;", "a d c")]
    [InlineData("group_uniforms a;\nuniform float x;\ngroup_uniforms a.b;\nuniform float y;\ngroup_uniforms a;\nuniform float z;", "x z y")]
    [InlineData("uniform int m : hint_enum(\"Off\", \"On {\");\nuniform float b;", "m b")]
    [InlineData("group_uniforms g;\nuniform float c;\ngroup_uniforms;\nuniform float d;\ngroup_uniforms g;\nuniform float e;", "c e d")]
    [InlineData("// see #12\nuniform float b;", "b")]
    [InlineData("/*\n#define X\n*/\nuniform float b;", "b")]
    public void LocalUniformsComeInGodotsOrder(string code, string expected)
    {
        ImmutableArray<string>? names = ShaderUniforms.Scan(code);

        Assert.NotNull(names);
        Assert.Equal(expected.Split(' '), names.Value);
    }

    // No compiler runs, so a preprocessor directive could add or hide a uniform the scan cannot see.
    [Theory]
    [InlineData("#define N 2\nuniform float a;")]
    [InlineData("#include \"res://i.gdshaderinc\"\nuniform float a;")]
    [InlineData("uniform float a;\n  #if FOO\nuniform float b;\n#endif")]
    [InlineData("/* note */ #include \"x.gdshaderinc\"\nuniform float a;")]
    [InlineData("\t#include \"x.gdshaderinc\"\nuniform float a;")]
    public void APreprocessorDirectiveGivesNoOrder(string code) => Assert.Null(ShaderUniforms.Scan(code));

    // ShaderCompiler::_dump_node_code (shader_compiler.cpp) skips a uniform with one of these hints before it adds it to
    // the uniforms ShaderMaterial lists, so it opens no group bucket.
    [Theory]
    [InlineData("hint_screen_texture")]
    [InlineData("hint_normal_roughness_texture")]
    [InlineData("hint_depth_texture")]
    [InlineData("hint_blit_source0")]
    [InlineData("hint_blit_source1")]
    [InlineData("hint_blit_source2")]
    [InlineData("hint_blit_source3")]
    public void AUniformOnAnEngineTextureHintIsNotListed(string hint)
    {
        string code =
            $"uniform sampler2D s : {hint}, filter_linear;\ngroup_uniforms c;\nuniform vec4 tint;\ngroup_uniforms;\nuniform float strength;";

        ImmutableArray<string>? names = ShaderUniforms.Scan(code);

        Assert.NotNull(names);
        Assert.Equal(["tint", "strength"], names.Value);
    }
}
