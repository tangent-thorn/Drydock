using Drydock.Core.Plugins;

namespace Drydock.Core.Sources;

/// <summary>What a cached read depends on: the source's written time (its -wal included), its status, and one setting or day.</summary>
public readonly record struct SourceKey(DateTime? WrittenUtc, PluginStatus Status, long Setting);

/// <summary>
/// The last read of one source, reused while its key is unchanged. The key is the file's time and not the read's
/// outcome, so a failed read is reused too, and a source that recovers writes a newer time and is read again.
/// </summary>
public sealed class ReadCache<T> where T : class
{
    private SourceKey? key;
    private T? value;

    public SourceKey? Key => key;

    public T Get(SourceKey k, Func<T> read)
    {
        if (key == k && value is not null) return value;
        var v = read();
        key = k;
        value = v;
        return v;
    }

    public void Clear()
    {
        key = null;
        value = null;
    }
}

/// <summary>A request during a run is remembered once and answered by one more run.</summary>
public sealed class RefreshGate
{
    private int state; // 0 idle, 1 running, 2 running with one more wanted

    /// <summary>True when the caller should start a run; false when one is running and has been told to go again.</summary>
    public bool TryStart()
    {
        while (true)
        {
            var s = Volatile.Read(ref state);
            if (s == 0)
            {
                if (Interlocked.CompareExchange(ref state, 1, 0) == 0) return true;
            }
            else if (s == 2 || Interlocked.CompareExchange(ref state, 2, 1) == 1) return false;
        }
    }

    /// <summary>True when another run was asked for during this one; the gate stays running for it.</summary>
    public bool FinishAndAskAgain()
    {
        if (Interlocked.CompareExchange(ref state, 0, 1) == 1) return false;
        Interlocked.Exchange(ref state, 1);
        return true;
    }
}
