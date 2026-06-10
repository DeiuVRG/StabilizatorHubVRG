namespace StabilizatorHub.Application.Abstractions;

/// <summary>
/// Anti brute-force guard for pairing-code claims. Keyed by caller identity
/// (user id or client IP); after too many failures the key is blocked for a
/// cool-down period.
/// </summary>
public interface IClaimAttemptLimiter
{
    bool IsBlocked(string key);

    void RecordFailure(string key);

    void RecordSuccess(string key);
}
