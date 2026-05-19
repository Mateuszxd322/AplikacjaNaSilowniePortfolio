using AplikacjaNaSiłownię.Data;
using AplikacjaNaSiłownię.Helpers;
using AplikacjaNaSiłownię.Hubs;
using AplikacjaNaSiłownię.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AplikacjaNaSiłownię.Services
{
    // Serwis powiadomień. Odpowiada za dwie kluczowe rzeczy:
    // 1. Zapisywanie powiadomień w bazie danych (żeby użytkownik mógł je zobaczyć po zalogowaniu).
    // 2. Wysyłanie powiadomień "na żywo" przez SignalR (jeśli użytkownik jest aktualnie na stronie).
    public class NotificationService(
        ApplicationDbContext context,
        IHubContext<NotificationHub> hubContext) : INotificationService
    {
        // _context pozwala nam rozmawiać z bazą danych (Entity Framework Core)
        private readonly ApplicationDbContext _context = context;
        
        // _hubContext pozwala nam wysyłać wiadomości przez SignalR bezpośrednio do przeglądarki użytkownika
        private readonly IHubContext<NotificationHub> _hubContext = hubContext;

        // Tworzy powiadomienie, zapisuje je w bazie danych i natychmiast wysyła użytkownikowi przez SignalR.
        public async Task<bool> SendNotificationAsync(string userId, string title, string message, string type)
        {
            try
            {
                // 1. Tworzymy nowy obiekt powiadomienia (rekord do bazy danych)
                var notification = new Notification
                {
                    UserId = userId,
                    Title = title,
                    Message = message,
                    Type = type,
                    CreatedAt = TimeHelper.GetPolishTime(), // Pobieramy aktualny czas w Polsce
                    IsRead = false // Nowo utworzone powiadomienie jest domyślnie NIEPRZECZYTANE
                };

                // 2. Dodajemy powiadomienie do bazy danych
                _context.Notifications.Add(notification);
                await _context.SaveChangesAsync(); // Zapisujemy zmiany w bazie danych

                // 3. SignalR: Wysyłamy powiadomienie w czasie rzeczywistym.
                // Clients.User(userId) sprawia, że powiadomienie trafi TYLKO do tego konkretnego zalogowanego użytkownika.
                // Metoda "ReceiveNotification" to nazwa zdarzenia, na które czeka kod JavaScript w przeglądarce.
                await _hubContext.Clients.User(userId).SendAsync("ReceiveNotification", new
                {
                    id = notification.Id,
                    title = notification.Title,
                    message = notification.Message,
                    type = notification.Type,
                    createdAt = notification.CreatedAt.ToString("dd.MM.yyyy HH:mm")
                });

                return true; // Sukces!
            }
            catch
            {
                return false; // W razie błędu zwracamy false
            }
        }

        // Pobiera z bazy danych wszystkie NIEPRZECZYTANE powiadomienia dla danego użytkownika.
        public async Task<List<Notification>> GetUnreadNotificationsAsync(string userId)
        {
            // Filtrujemy powiadomienia po UserId i IsRead == false.
            // OrderByDescending sprawia, że najnowsze powiadomienia będą na samej górze listy.
            return await _context.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();
        }

        // Oznacza pojedyncze powiadomienie jako przeczytane.
        public async Task<bool> MarkAsReadAsync(int notificationId, string userId)
        {
            // Szukamy w bazie powiadomienia o podanym ID, upewniając się, że należy do zalogowanego użytkownika (zabezpieczenie!)
            var notification = await _context.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);
            if (notification == null) return false;

            // Zmieniamy flagę na true (przeczytane) i zapisujemy w bazie
            notification.IsRead = true;
            await _context.SaveChangesAsync();
            return true;
        }

        // Oznacza wszystkie powiadomienia użytkownika jako przeczytane za jednym razem.
        public async Task<bool> MarkAllAsReadAsync(string userId)
        {
            // Pobieramy z bazy listę wszystkich nieprzeczytanych powiadomień użytkownika
            var notifications = await _context.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync();
            
            // Pętla foreach przechodzi przez każde powiadomienie i ustawia IsRead na true
            foreach (var n in notifications)
            {
                n.IsRead = true;
            }
            
            // Zapisujemy wszystkie zmiany w bazie danych za pomocą jednego zapytania SQL
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
