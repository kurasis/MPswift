using Player.Core.Library;

namespace Player.App.Services.Storage;

/// <summary>Internal optimized session writes; external IPlayerStore implementations retain the full-state fallback.</summary>
internal interface ISessionPlayerStore
{
    Task SaveSessionAsync(SessionState session);
}
