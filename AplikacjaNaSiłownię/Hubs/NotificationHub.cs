using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AplikacjaNaSiłownię.Hubs
{
    [Authorize]
    public class NotificationHub : Hub
    {
    }
}