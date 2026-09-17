namespace ZapperRadio.Core.Audio;

/// <summary>
/// Decides whether a station that does not mark its ads is in an ad break. A song that runs well past its length
/// suggests one, but the station may also be playing a longer version or a next song it sent no title for.
/// While the stream is being listened to, the break is therefore only assumed once speech is heard, and it ends
/// again after a minute of uninterrupted music. Without listening, the overdue song alone decides.
/// </summary>
public sealed class UnmarkedAdBreak
{
    /// <summary>Windows of music in a row (about a minute) after which the station is playing a song again.</summary>
    public const int MusicWindowsToEnd = 12;

    private bool _speechHeard;

    public bool IsActive { get; private set; }

    /// <returns>Whether <see cref="IsActive"/> changed.</returns>
    public bool Update(bool isSongOverdue, bool isListening, SoundHistory sound)
    {
        if (!isSongOverdue)
        {
            _speechHeard = false;
        }
        else if (sound.Current == Sound.Speech)
        {
            _speechHeard = true;
        }
        else if (sound.ConsecutiveMusic >= MusicWindowsToEnd)
        {
            _speechHeard = false;
        }

        var isActive = isSongOverdue && (_speechHeard || !isListening);
        var changed = isActive != IsActive;
        IsActive = isActive;
        return changed;
    }
}
