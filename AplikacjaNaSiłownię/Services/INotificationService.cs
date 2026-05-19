using AplikacjaNaSiłownię.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AplikacjaNaSiłownię.Services
{
    public interface INotificationService
    {
        Task<bool> SendNotificationAsync(string userId, string title, string message, string type);
        Task<List<Notification>> GetUnreadNotificationsAsync(string userId);
        Task<bool> MarkAsReadAsync(int notificationId, string userId);
        Task<bool> MarkAllAsReadAsync(string userId);
    }
}
