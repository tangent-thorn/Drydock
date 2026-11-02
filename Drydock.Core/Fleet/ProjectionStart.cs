namespace Drydock.Core.Fleet;

/// <summary>Where the projection starts when AutoRetainer's gil cannot be read.</summary>
/// <param name="Position">The starting position.</param>
/// <param name="Note">The one line that says where it came from.</param>
public sealed record ProjectionStartView(long Position, string Note);

public static class ProjectionStart
{
    public const string From0 = "Starting from 0: needs AutoRetainer for gil";
    public const string FromSalvage = "Starting from unsold salvage only: needs AutoRetainer for gil";

    /// <summary>The unsold salvage when Allagan Tools gave a backlog, else 0.</summary>
    public static ProjectionStartView WithoutGil(SalvageBacklog? backlog) =>
        backlog is null ? new(0, From0) : new(backlog.TotalValue, FromSalvage);
}
