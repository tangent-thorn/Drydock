using Drydock.Core.Fleet;

namespace Drydock.Core.Plugins;

/// <summary>
/// Decides when the setup window opens by itself: once per session, and only after the other plugins have had time to
/// load since the login. A user who closes it keeps it closed until the setup command.
/// </summary>
public sealed class SetupAutoOpen(TimeSpan grace)
{
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(30);

    private DateTime? loggedInAt;
    private bool decided;

    public SetupAutoOpen() : this(DefaultGrace) { }

    /// <summary>Starts (or restarts) the wait; a new login gives the plugins time to load again.</summary>
    public void NoteLogin(DateTime nowUtc) => loggedInAt = nowUtc;

    public void NoteLogout() => loggedInAt = null;

    /// <summary>True at most once: when the wait is over, results exist and a required plugin is not ready.</summary>
    public bool ShouldOpen(DateTime nowUtc, bool haveResults, bool requiredReady)
    {
        if (decided || loggedInAt is not { } since || nowUtc - since < grace || !haveResults) return false;
        if (requiredReady) return false;
        decided = true;
        return true;
    }
}

/// <summary>What to set on the server info bar entry: only what moved since the last call.</summary>
public readonly record struct InfoBarDelta(bool SetText, bool SetTooltip, bool SetShown, bool Shown);

public sealed class InfoBarState
{
    private bool? shown;
    private string? text;
    private string? tooltip;

    public InfoBarDelta Next(InfoBarView view)
    {
        var show = view.Text is not null;
        var setShown = shown != show;
        shown = show;
        if (!show) return new(false, false, setShown, false);

        var setText = text != view.Text;
        var setTooltip = tooltip != view.Tooltip;
        text = view.Text;
        tooltip = view.Tooltip;
        return new(setText, setTooltip, setShown, true);
    }

    /// <summary>The entry could not be updated and is hidden; the next view sets everything again.</summary>
    public void Reset()
    {
        shown = false;
        text = null;
        tooltip = null;
    }
}

/// <summary>Messages already logged, each once; the set is cleared at its cap so it cannot grow for ever.</summary>
public sealed class LogOnce(int cap = 100)
{
    private readonly HashSet<string> seen = [];
    private readonly object gate = new();

    /// <summary>True the first time a message is seen.</summary>
    public bool First(string message)
    {
        lock (gate)
        {
            if (seen.Contains(message)) return false;
            if (seen.Count >= cap) seen.Clear();
            seen.Add(message);
            return true;
        }
    }
}
