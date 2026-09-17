namespace WinRadioPlayer.Core.Shell;

public enum JumpListAction
{
    Play,
    Mute,
    Unmute,
}

/// <summary>What a taskbar jump list item asks the app to do, passed as command-line arguments.</summary>
public sealed record JumpListCommand(JumpListAction Action, string? Url = null)
{
    private const string PlayArgument = "--play";
    private const string MuteArgument = "--mute";
    private const string UnmuteArgument = "--unmute";

    public static JumpListCommand Play(string url) => new(JumpListAction.Play, url);

    public string ToArguments() => Action switch
    {
        // A quote cannot appear in a valid URL, but would break the command line.
        JumpListAction.Play => $"{PlayArgument} \"{Url?.Replace("\"", "%22")}\"",
        JumpListAction.Mute => MuteArgument,
        _ => UnmuteArgument,
    };

    /// <summary>Finds a command in the arguments, which may also contain the program path.</summary>
    public static JumpListCommand? Parse(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case PlayArgument when i + 1 < args.Count && args[i + 1].Length > 0:
                    return Play(args[i + 1]);
                case MuteArgument:
                    return new JumpListCommand(JumpListAction.Mute);
                case UnmuteArgument:
                    return new JumpListCommand(JumpListAction.Unmute);
            }
        }

        return null;
    }

    /// <summary>The jump list line for a favorite: its name, followed by the current song if known.</summary>
    public static string Title(string name, string song, int maxLength = 100)
    {
        var title = song.Length > 0 ? $"{name} · {song}" : name;
        return title.Length > maxLength ? title[..(maxLength - 1)].TrimEnd() + "…" : title;
    }
}
