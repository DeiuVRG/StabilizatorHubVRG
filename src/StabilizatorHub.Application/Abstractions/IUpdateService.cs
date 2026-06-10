using StabilizatorHub.Application.Common;
using StabilizatorHub.Application.Dtos;

namespace StabilizatorHub.Application.Abstractions;

/// <summary>Port for checking whether a newer application release exists (GitHub Releases).</summary>
public interface IUpdateChecker
{
    Task<UpdateInfoDto> CheckAsync(CancellationToken ct = default);
}

/// <summary>
/// Port for requesting a self-update. The implementation writes a trigger file
/// watched by a separate systemd updater unit, so the app never restarts itself
/// with elevated privileges.
/// </summary>
public interface IUpdateTrigger
{
    Task<OperationResult> RequestUpdateAsync(string requestedBy, CancellationToken ct = default);
}
