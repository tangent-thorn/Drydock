using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Drydock;

// AutoRetainer keeps a changed setting in memory and writes its file later, so the file can be hours behind.
// Its IPC answers with what it holds now. Each call runs on the framework thread, where AutoRetainer edits that data.
internal sealed class AutoRetainerIpc(IDalamudPluginInterface pi, IFramework framework)
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

    // Dalamud hands AutoRetainer's object over as JSON into this type; only the fields named here are kept.
    private sealed class CharacterFlags
    {
        public bool ExcludeWorkshop { get; set; }
    }

    private string? lastFailure;

    /// <summary>ExcludeWorkshop per registered CID as AutoRetainer holds it now; null when AutoRetainer does not answer.</summary>
    public IReadOnlyDictionary<ulong, bool>? ReadExclusions(CancellationToken ct)
    {
        try
        {
            var cidsGate = pi.GetIpcSubscriber<List<ulong>>("AutoRetainer.GetRegisteredCIDs");
            var dataGate = pi.GetIpcSubscriber<ulong, CharacterFlags?>("AutoRetainer.GetOfflineCharacterData");
            var cids = OnFramework(() => cidsGate.InvokeFunc(), ct);
            var live = new Dictionary<ulong, bool>();
            // one character per call: each converts AutoRetainer's whole record, so the frames share the work
            foreach (var cid in cids ?? [])
            {
                if (OnFramework(() => dataGate.InvokeFunc(cid), ct) is { } c) live[cid] = c.ExcludeWorkshop;
            }
            lastFailure = null;
            return live;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = ex.GetType().Name + ": " + ex.Message;
            if (lastFailure != message)
            {
                lastFailure = message;
                Plugin.Log.Debug("Drydock reads AutoRetainer's file alone, its IPC did not answer: " + message);
            }
            return null;
        }
    }

    private T OnFramework<T>(Func<T> call, CancellationToken ct) =>
        framework.RunOnFrameworkThread(call).WaitAsync(CallTimeout, ct).GetAwaiter().GetResult();
}
