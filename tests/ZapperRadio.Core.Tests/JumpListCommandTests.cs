using ZapperRadio.Core.Shell;

namespace ZapperRadio.Core.Tests;

public class JumpListCommandTests
{
    [Fact]
    public void Play_RoundTripsThroughArguments()
    {
        var command = JumpListCommand.Play("http://radio.example/stream?a=1&b=2");

        Assert.Equal("--play \"http://radio.example/stream?a=1&b=2\"", command.ToArguments());
        Assert.Equal(command, JumpListCommand.Parse([@"C:\Program Files\ZapperRadio\ZapperRadio.exe", "--play", "http://radio.example/stream?a=1&b=2"]));
    }

    [Theory]
    [InlineData("--mute", JumpListAction.Mute)]
    [InlineData("--UNMUTE", JumpListAction.Unmute)]
    public void Parse_FindsMuteCommands(string argument, JumpListAction expected)
    {
        Assert.Equal(new JumpListCommand(expected), JumpListCommand.Parse(["ZapperRadio.exe", argument]));
    }

    [Fact]
    public void Parse_IgnoresUnknownOrIncompleteArguments()
    {
        Assert.Null(JumpListCommand.Parse(["ZapperRadio.exe"]));
        Assert.Null(JumpListCommand.Parse(["ZapperRadio.exe", "--play"]));
        Assert.Null(JumpListCommand.Parse(["ZapperRadio.exe", "--volume", "10"]));
    }

    [Fact]
    public void Title_AddsSongAndShortensLongTitles()
    {
        Assert.Equal("Radio 538", JumpListCommand.Title("Radio 538", ""));
        Assert.Equal("Radio 538 · Artist - Song", JumpListCommand.Title("Radio 538", "Artist - Song"));
        Assert.Equal("Radio 538 · Artist…", JumpListCommand.Title("Radio 538", "Artist - Song", maxLength: 19));
    }
}
