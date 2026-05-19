using AplikacjaNaSiłownię.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Threading.Tasks;
using AplikacjaNaSiłownię.Models;

namespace AplikacjaNaSiłownię.Controllers
{
    // Kontroler API dla powiadomień. 
    // W przeciwieństwie do tradycyjnych kontrolerów MVC (które zwracają całe widoki HTML), 
    // ten kontroler jest typu "ControllerBase" i zwraca tylko czyste dane w formacie JSON (czyli API).
    // Kod JavaScript w przeglądarce odpytuje te metody (tzw. Endpointy), pobiera dane i dynamicznie aktualizuje stronę.
    [Authorize] // Blokuje dostęp dla niezalogowanych użytkowników. Tylko zalogowani mogą pytać o swoje powiadomienia!
    [Route("api/[controller]")] // Automatycznie mapuje trasę na "api/notification". Wygodne i uporządkowane.
    [ApiController] // Oznacza klasę jako kontroler API (automatycznie waliduje modele, ułatwia obsługę błędów)
    public class NotificationController(
        INotificationService notificationService,
        UserManager<ApplicationUser> userManager) : ControllerBase
    {
        private readonly INotificationService _notificationService = notificationService;
        private readonly UserManager<ApplicationUser> _userManager = userManager;

        // GET: api/notification/unread
        // Pobiera listę nieprzeczytanych powiadomień zalogowanego użytkownika w formacie JSON.
        [HttpGet("unread")]
        public async Task<IActionResult> GetUnreadNotifications()
        {
            // 1. Pobieramy unikalne ID zalogowanego użytkownika, który wysłał zapytanie
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized(); // 401: Brak autoryzacji

            // 2. Pobieramy listę powiadomień z serwisu
            var notifications = await _notificationService.GetUnreadNotificationsAsync(userId);
            
            // 3. Mapujemy (czyli przepisujemy) obiekty z bazy danych na prostszy, bezpieczniejszy format (tzw. Anonimowy Obiekt).
            // Dzięki temu przesyłamy przez sieć tylko te dane, które są naprawdę potrzebne w przeglądarce, 
            // a datę formatujemy na ładny napis, np. "18.05.2026 20:30".
            var result = notifications.Select(n => new
            {
                id = n.Id,
                title = n.Title,
                message = n.Message,
                type = n.Type,
                createdAt = n.CreatedAt.ToString("dd.MM.yyyy HH:mm")
            });

            // 4. Zwracamy status HTTP 200 OK wraz ze zmapowaną listą powiadomień w formacie JSON.
            return Ok(result);
        }

        // POST: api/notification/mark-read
        // Oznacza pojedyncze powiadomienie jako przeczytane.
        // Parametr 'id' jest przesyłany w ciele zapytania (FromBody).
        [HttpPost("mark-read")]
        [ValidateAntiForgeryToken] // Zabezpieczenie CSRF
        public async Task<IActionResult> MarkAsRead([FromBody] int id)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            // Przekazujemy zadanie do serwisu powiadomień
            var success = await _notificationService.MarkAsReadAsync(id, userId);
            
            // Zwracamy obiekt JSON { success: true/false }
            return Ok(new { success });
        }

        // POST: api/notification/mark-all-read
        // Oznacza wszystkie powiadomienia zalogowanego użytkownika jako przeczytane.
        [HttpPost("mark-all-read")]
        [ValidateAntiForgeryToken] // Zabezpieczenie CSRF
        public async Task<IActionResult> MarkAllAsRead()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            // Przekazujemy zadanie do serwisu powiadomień
            var success = await _notificationService.MarkAllAsReadAsync(userId);
            
            // Zwracamy obiekt JSON { success: true/false }
            return Ok(new { success });
        }
    }
}
