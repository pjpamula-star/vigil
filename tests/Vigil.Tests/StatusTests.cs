using Vigil.UI;

namespace Vigil.Tests;

public class StatusTests
{
    [Theory]
    [InlineData("2", 120)]
    [InlineData("2h", 120)]
    [InlineData("1.5", 90)]
    [InlineData("30m", 30)]
    [InlineData("90 min", 90)]
    [InlineData("1:30", 90)]
    public void Parses_what_you_type(string text, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), Status.ParseDuration(text));

    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("0")]
    [InlineData("48")]
    public void Rejects_nonsense(string text) => Assert.Null(Status.ParseDuration(text));
}
