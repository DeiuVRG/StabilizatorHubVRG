using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using StabilizatorHub.Application.Services;
using StabilizatorHub.Web.Extensions;

namespace StabilizatorHub.Web.Hubs;

/// <summary>
/// Real-time channel towards the dashboard. On connect, the client is added to
/// one group per owned device; broadcasts therefore reach only the owner.
/// </summary>
[Authorize]
public sealed class LiveHub : Hub
{
    public static string DeviceGroup(string deviceId) => $"device:{deviceId.ToUpperInvariant()}";

    private readonly IDeviceQueryService _deviceQuery;
    private readonly IDeviceAccessService _deviceAccess;

    public LiveHub(IDeviceQueryService deviceQuery, IDeviceAccessService deviceAccess)
    {
        _deviceQuery = deviceQuery;
        _deviceAccess = deviceAccess;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.GetUserId();

        if (userId is not null)
        {
            foreach (var device in await _deviceQuery.GetMineAsync(userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, DeviceGroup(device.Id));
            }
        }

        await base.OnConnectedAsync();
    }

    /// <summary>Joins the group of a freshly attached device without reconnecting (access is re-checked).</summary>
    public async Task JoinDevice(string deviceId)
    {
        var userId = Context.User?.GetUserId();

        if (userId is null)
        {
            return;
        }

        var access = await _deviceAccess.GetAccessibleDeviceAsync(userId, deviceId);

        if (access.Succeeded)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, DeviceGroup(deviceId));
        }
    }
}
