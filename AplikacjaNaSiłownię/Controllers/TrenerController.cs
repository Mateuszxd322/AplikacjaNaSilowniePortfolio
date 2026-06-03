using AplikacjaNaSiłownię.Data;
using AplikacjaNaSiłownię.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace AplikacjaNaSiłownię.Controllers
{
    [Authorize(Roles = "Trener")]
    // Używamy Primary Constructor (C# 12) do automatycznego przypisania serwisów do pól prywatnych.
    public class TrenerController(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context,
        Services.IUserService userService,
        Services.IWorkoutService workoutService,
        Services.INotificationService notificationService) : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager = userManager;
        private readonly ApplicationDbContext _context = context;
        private readonly Services.IUserService _userService = userService;
        private readonly Services.IWorkoutService _workoutService = workoutService;
        private readonly Services.INotificationService _notificationService = notificationService;

        public async Task<IActionResult> Index()
        {
            var trenerId = _userManager.GetUserId(User);
            // OPTYMALIZACJA EF CORE: Filtrujemy dane po stronie SQL (.Where przed .ToListAsync()).
            // Pozwala to uniknąć pobierania wszystkich użytkowników do pamięci RAM serwera.
            var clients = await _userManager.Users
                .Where(u => u.Role == "Klient" && u.TrainerId == trenerId)
                .ToListAsync();

            return View(clients);
        }

        [HttpGet]
        public async Task<IActionResult> AvailableClients()
        {
            var trenerId = _userManager.GetUserId(User);

            // OPTYMALIZACJA: Wykonujemy dwa precyzyjne zapytania do bazy danych zamiast jednego ogólnego.
            // Pokaż tylko tych, którzy NIE MAJĄ trenera ANI zaproszenia oczekującego
            var availableClients = await _userManager.Users
                .Where(u => u.Role == "Klient" && string.IsNullOrEmpty(u.TrainerId) && string.IsNullOrEmpty(u.PendingTrainerId))
                .ToListAsync();

            ViewBag.SentRequests = await _userManager.Users
                .Where(u => u.Role == "Klient" && u.PendingTrainerId == trenerId)
                .ToListAsync();

            return View(availableClients);
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // Zabezpieczenie przed atakami CSRF (Cross-Site Request Forgery). Zapobiega podszywaniu się pod formularze.
        public async Task<IActionResult> AssignClient([FromBody] string clientId)
        {
            // 1. Walidacja danych wejściowych – jeśli ID klienta jest puste, zwracamy błąd 400 Bad Request
            if (string.IsNullOrEmpty(clientId)) return BadRequest();

            // 2. Pobieramy ID aktualnie zalogowanego trenera
            var trenerId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(trenerId)) return Unauthorized(); // Jeśli trener nie jest zalogowany, zwracamy 401 Unauthorized

            // 3. Wyszukujemy klienta w bazie danych po jego identyfikatorze
            var client = await _userManager.FindByIdAsync(clientId);

            // 4. Warunek bezpieczeństwa:
            // Upewniamy się, że:
            // - Klient istnieje (client != null)
            // - Rzeczywiście ma rolę "Klient"
            // - Nie ma jeszcze trenera (client.TrenerId jest puste)
            // - Nie oczekuje już na zaproszenie od innego trenera (client.PendingTrenerId jest puste)
            if (client != null && client.Role == "Klient" && string.IsNullOrEmpty(client.TrainerId) && string.IsNullOrEmpty(client.PendingTrainerId))
            {
                // 5. Wysyłamy zaproszenie – ustawiamy pole 'PendingTrenerId' na ID tego trenera
                client.PendingTrainerId = trenerId;
                await _userManager.UpdateAsync(client); // Zapisujemy aktualizację w bazie danych Identity

                // 6. Pobieramy pełne dane trenera, aby ładnie sformatować treść powiadomienia
                var trener = await _userManager.FindByIdAsync(trenerId);

                // 7. Wysyłamy powiadomienie do klienta w czasie rzeczywistym przez nasz serwis powiadomień
                await _notificationService.SendNotificationAsync(
                    client.Id,
                    "Nowe zaproszenie od trenera",
                    $"Trener {trener?.FirstName} {trener?.LastName} zaprosił Cię do współpracy. Przejdź do zakładki 'Moje Zaproszenia', aby zaakceptować lub odrzucić zaproszenie.",
                    "Zaproszenie"
                );

                // 8. Zwracamy obiekt JSON potwierdzający sukces (success = true), który obsłuży kod JavaScript na stronie
                return Json(new { success = true });
            }

            // Jeśli któryś z warunków nie był spełniony, zwracamy informację o błędzie w formacie JSON
            return Json(new { success = false, message = "Klient nie istnieje, ma już trenera, lub oczekuje na zaproszenie od innego trenera." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // Zabezpieczenie przed atakami CSRF
        public async Task<IActionResult> CancelRequest([FromBody] string clientId)
        {
            if (string.IsNullOrEmpty(clientId)) return BadRequest();

            var trenerId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(trenerId)) return Unauthorized();

            var client = await _userManager.FindByIdAsync(clientId);

            // Weryfikujemy czy klient istnieje, ma rolę "Klient" oraz czy zaproszenie wysłał dokładnie ten trener, który teraz próbuje je cofnąć
            if (client != null && client.Role == "Klient" && client.PendingTrainerId == trenerId)
            {
                // Zerujemy pole zaproszenia (ustawiamy na null), co oznacza anulowanie zaproszenia
                client.PendingTrainerId = null;
                await _userManager.UpdateAsync(client); // Zapisujemy zmiany

                var trener = await _userManager.FindByIdAsync(trenerId);

                // Wysyłamy powiadomienie do klienta, że zaproszenie zostało wycofane
                await _notificationService.SendNotificationAsync(
                    client.Id,
                    "Zaproszenie cofnięte",
                    $"Trener {trener?.FirstName} {trener?.LastName} wycofał zaproszenie do współpracy.",
                    "Zaproszenie"
                );

                return Json(new { success = true });
            }

            return Json(new { success = false, message = "Nie można anulować tego zaproszenia. Prawdopodobnie klient już na nie zareagował lub zaproszenie wygasło." });
        }

        public class RemoveClientRequestDto
        {
            public string ClientId { get; set; } = string.Empty;

            [StringLength(500, ErrorMessage = "Powód nie może być dłuższy niż 500 znaków.")]
            public string Reason { get; set; } = string.Empty;
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // Zabezpieczenie przed atakami CSRF
        public async Task<IActionResult> RemoveClient([FromBody] RemoveClientRequestDto request)
        {
            // Weryfikacja wejściowa: Upewniamy się, że obiekt żądania oraz identyfikator klienta nie są puste.
            if (request == null || string.IsNullOrEmpty(request.ClientId))
                return BadRequest("Brak ID klienta.");

            // LOGIKA ELASTYCZNA (UX): Jeśli trener nie wpisał powodu w oknie dialogowym, 
            // nie odrzucamy żądania błędem 400. Zamiast tego podstawiamy elegancki tekst domyślny.
            var reason = string.IsNullOrWhiteSpace(request.Reason) ? "Brak podanego powodu." : request.Reason;

            // OPTYMALIZACJA BAZODANOWA & OCHRONA BUFORA: Jeśli powód jest zbyt długi (np. wstrzyknięty z zewnątrz),
            // zamiast błędu bazy danych, bezpiecznie przycinamy go do dopuszczalnych 500 znaków.
            // Używamy nowoczesnego operatora zakresu C# [..500] (Range Operator) zalecanego przez IDE zamiast Substring.
            if (reason.Length > 500)
            {
                reason = reason[..500];
            }

            // Wywołujemy bezpieczną metodę z serwisu (z obsługą transakcji i weryfikacją trenera - ochrona IDOR)
            var success = await _userService.TerminateCollaborationAsync(request.ClientId, reason, "Trener", _userManager.GetUserId(User) ?? "");

            if (success) return Json(new { success = true });

            return Json(new { success = false, message = "Nie można odłączyć tego klienta (brak relacji lub błąd bazy)." });
        }

        [HttpGet]
        public async Task<IActionResult> History()
        {
            var trenerId = _userManager.GetUserId(User);
            var history = await _context.CollaborationHistories
                .Where(h => h.TrainerId == trenerId && !h.IsDeletedByTrainer)
                .OrderByDescending(h => h.EndDate)
                .ToListAsync();
            return View(history);
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // Zabezpieczenie przed atakami CSRF
        public async Task<IActionResult> DeleteHistoryRecord([FromBody] int id)
        {
            var trenerId = _userManager.GetUserId(User);
            if (trenerId == null) return Unauthorized();

            var record = await _context.CollaborationHistories.FindAsync(id);
            if (record != null && record.TrainerId == trenerId)
            {
                record.IsDeletedByTrainer = true;

                // ZABEZPIECZENIE: Jeśli klient nie istnieje (ClientId to null), od razu usuwamy rekord fizycznie, zapobiegając powstawaniu śmieci w bazie.
                if (record.IsDeletedByTrainer && (record.IsDeletedByClient || string.IsNullOrEmpty(record.ClientId)))
                {
                    _context.CollaborationHistories.Remove(record);
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            return Json(new { success = false });
        }

        [HttpGet]
        public async Task<IActionResult> ClientCalendar(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();

            var trenerId = _userManager.GetUserId(User);
            var client = await _userManager.FindByIdAsync(id);

            // Weryfikacja: trener może podglądać tylko swoich klientów
            if (client == null || client.TrainerId != trenerId)
            {
                return Unauthorized("Brak dostępu do tego kalendarza.");
            }

            ViewData["ClientName"] = $"{client.FirstName} {client.LastName}";
            ViewData["ClientId"] = client.Id;

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetClientWorkouts(string clientId)
        {
            var trenerId = _userManager.GetUserId(User);
            var client = await _userManager.FindByIdAsync(clientId);

            if (client == null || client.TrainerId != trenerId) return Unauthorized();

            var dbWorkouts = await _workoutService.GetClientWorkoutsAsync(clientId);

            var workouts = dbWorkouts.Select(w =>
            {
                return new
                {
                    id = w.Id,
                    title = w.Title,
                    start = w.StartDate.ToString("yyyy-MM-ddTHH:mm:ss"),
                    end = w.EndDate.HasValue ? w.EndDate.Value.ToString("yyyy-MM-ddTHH:mm:ss") : null,
                    description = w.Description,
                    status = w.GetFinalStatus(), // Wykorzystujemy scentralizowaną logikę z modelu
                    className = w.GetCssClass() // Wykorzystujemy scentralizowaną logikę z modelu
                };
            }).ToList();

            return Json(workouts);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        // Akcja Trenera do zapisywania nowych treningów dla swoich przypisanych klientów.
        // Serwis weryfikuje relację trener-klient przed zapisem w bazie.
        public async Task<IActionResult> SaveWorkout([FromBody] PlannedWorkout model)
        {
            ModelState.Remove("Client"); // ClientId jest wymagany (bo trener wybiera klienta), ale nawigacyjny "Client" nie.

            if (!ModelState.IsValid)
            {
                var errors = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return Json(new { success = false, message = errors });
            }
            var success = await _workoutService.AddWorkoutAsync(model, _userManager.GetUserId(User) ?? string.Empty, "Trener");
            return Json(new { success, id = model.Id, message = success ? "" : "Błąd: Czas rozpoczęcia nie może być w przeszłości lub brak relacji trener-klient." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditWorkout([FromBody] PlannedWorkout model)
        {
            ModelState.Remove("Client"); // ClientId jest wymagany (bo trener edytuje klienta), ale nawigacyjny "Client" nie.

            if (!ModelState.IsValid)
            {
                var errors = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return Json(new { success = false, message = errors });
            }
            var success = await _workoutService.UpdateWorkoutAsync(model, _userManager.GetUserId(User) ?? string.Empty, "Trener");
            return Json(new { success, message = success ? "" : "Błąd: Nie można edytować treningu na czas przeszły lub brak uprawnień." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteWorkout([FromBody] int id)
        {
            var success = await _workoutService.DeleteWorkoutAsync(id, _userManager.GetUserId(User) ?? string.Empty, "Trener");
            return Json(new { success, message = success ? "" : "Błąd: Nie można odwołać treningu." });
        }
    }
}
