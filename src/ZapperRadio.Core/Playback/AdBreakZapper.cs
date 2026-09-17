namespace ZapperRadio.Core.Playback;

/// <summary>What a station stream is doing right now, as far as zapping is concerned.</summary>
public enum ChannelState
{
    /// <summary>Not playing: connecting, buffering, reconnecting or failed.</summary>
    Unavailable,

    /// <summary>In an ad break, marked by the station or assumed from an overdue song.</summary>
    Ad,

    /// <summary>Playing, but without a title, so it is unknown what.</summary>
    Unknown,

    /// <summary>Playing a title that is not an ad.</summary>
    Song,
}

public readonly record struct Channel(string Url, ChannelState State);

/// <summary>
/// Decides when to zap away from an ad break and when to zap back. When the station being listened to starts
/// an ad break, it zaps to the highest favorite that plays a song (or else the highest one that at least
/// plays something). As soon as the station zapped away from plays a song again, it zaps back.
/// Picking a station yourself ends the zapping: that station stays on, even if it is in an ad break right then.
/// </summary>
public sealed class AdBreakZapper
{
    /// <summary>Ad breaks rarely last this long; after that, returning would only interrupt the station you ended up on.</summary>
    public static readonly TimeSpan MaxAdBreak = TimeSpan.FromMinutes(10);

    private DateTimeOffset _zappedAt;

    /// <summary>The station an ad break was zapped away from, which is returned to when it plays a song again.</summary>
    public string? ZappedFrom { get; private set; }

    /// <summary>A station picked during its ad break, whose break is therefore not zapped away from.</summary>
    public string? KeptDuringAd { get; private set; }

    /// <summary>Call when you pick a station yourself.</summary>
    public void OnPicked(string url, ChannelState state)
    {
        ZappedFrom = null;
        KeptDuringAd = state == ChannelState.Ad ? url : null;
    }

    public void OnStopped()
    {
        ZappedFrom = null;
        KeptDuringAd = null;
    }

    /// <summary>
    /// Returns the station to zap to, or null to stay. Call it whenever a stream changes.
    /// </summary>
    /// <param name="active">The station being listened to.</param>
    /// <param name="favorites">The favorites in list order; only these can be zapped to, because their streams stay open.</param>
    public string? Next(Channel active, IReadOnlyList<Channel> favorites, DateTimeOffset now)
    {
        if (KeptDuringAd is { } kept && StateOf(kept, active, favorites) != ChannelState.Ad)
        {
            KeptDuringAd = null;
        }

        if (ZappedFrom is { } origin)
        {
            if (origin == active.Url || now - _zappedAt > MaxAdBreak || !favorites.Any(f => f.Url == origin))
            {
                ZappedFrom = null;
            }
            else if (StateOf(origin, active, favorites) == ChannelState.Song)
            {
                ZappedFrom = null;
                return origin;
            }
        }

        if (active.State != ChannelState.Ad || active.Url == KeptDuringAd)
        {
            return null;
        }

        var next = favorites.FirstOrDefault(f => f.Url != active.Url && f.State == ChannelState.Song).Url
                   ?? favorites.FirstOrDefault(f => f.Url != active.Url && f.State == ChannelState.Unknown).Url;
        if (next is not null && ZappedFrom is null)
        {
            ZappedFrom = active.Url;
            _zappedAt = now;
        }

        return next;
    }

    private static ChannelState StateOf(string url, Channel active, IReadOnlyList<Channel> favorites) =>
        url == active.Url ? active.State : favorites.FirstOrDefault(f => f.Url == url) is { Url: not null } found ? found.State : ChannelState.Unavailable;
}
