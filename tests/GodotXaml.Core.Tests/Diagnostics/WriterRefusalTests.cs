using GodotXaml.Core.Diagnostics;

namespace GodotXaml.Core.Tests.Diagnostics;

public sealed class WriterRefusalTests
{
    [Theory]
    [InlineData(typeof(NotSupportedException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(ArgumentOutOfRangeException))]
    public void AnExceptionTheWriterRefusesWithIsARefusal(Type type) => Assert.True(WriterRefusal.Is((Exception)Activator.CreateInstance(type)!));

    [Theory]
    [InlineData(typeof(ArgumentNullException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(FormatException))]
    public void AnExceptionThatIsABugIsNotARefusal(Type type) => Assert.False(WriterRefusal.Is((Exception)Activator.CreateInstance(type)!));

    [Fact]
    public void ANullExceptionThrows() => Assert.Throws<ArgumentNullException>(() => WriterRefusal.Is(null!));
}
