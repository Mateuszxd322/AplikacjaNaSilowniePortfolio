using AplikacjaNaSiłownię.Data;
using AplikacjaNaSiłownię.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
namespace AplikacjaNaSiłownię.Controllers
{
    [Authorize(Roles = "Klient")]
    // Wykorzystujemy Primary Constructor (C# 12) do wstrzykiwania zależności.
    public class KlientController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        Services.IWorkoutService workoutService,
        Services.IUserService userService,
        Services.INotificationService notificationService) : Controller
    {
        private readonly ApplicationDbContext _context = context;
        private readonly UserManager<ApplicationUser> _userManager = userManager;
        private readonly Services.IWorkoutService _workoutService = workoutService;
        private readonly Services.IUserService _userService = userService;
        private readonly Services.INotificationService _notificationService = notificationService;

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Requests()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

            ApplicationUser? pendingTrener = null;
            if (!string.IsNullOrEmpty(user.PendingTrainerId))
            {
                pendingTrener = await _userManager.FindByIdAsync(user.PendingTrainerId);

                // Self-healing: Jeśli trener, który wysłał zaproszenie, został usunięty z bazy
                if (pendingTrener == null)
                {
                    user.PendingTrainerId = null;
                    await _userManager.UpdateAsync(user);
                }
            }

            return View(pendingTrener);
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // Zabezpieczenie przed atakami CSRF
        public async Task<IActionResult> AcceptRequest()
        {
            // 1. Pobieramy pełne dane zalogowanego klienta
            var user = await _userManager.GetUserAsync(User);

            // 2. Warunek: Upewniamy się, że klient istnieje i rzeczywiście ma zaproszenie oczekujące na akceptację (PendingTrenerId nie jest puste)
            if (user != null && !string.IsNullOrEmpty(user.PendingTrainerId))
            {
                // 3. Przypisujemy trenera: Ustawiamy stałe pole 'TrenerId' na ID trenera, który wysłał zaproszenie
                user.TrainerId = user.PendingTrainerId;

                // 4. Czyścimy pole zaproszenia (ustawiamy PendingTrenerId na null), ponieważ zaproszenie zostało pomyślnie zrealizowane
                user.PendingTrainerId = null;

                // 5. Zapisujemy zmiany w bazie danych
                await _userManager.UpdateAsync(user);

                // 6. Wysyłamy powiadomienie do trenera, żeby dowiedział się na żywo, że klient zaakceptował jego zaproszenie
                if (!string.IsNullOrEmpty(user.TrainerId))
                {
                    await _notificationService.SendNotificationAsync(
                        user.TrainerId,
                        "Zaproszenie zaakceptowane!",
                        $"Klient {user.FirstName} {user.LastName} zaakceptował Twoje zaproszenie do współpracy. Od teraz możecie razem planować treningi!",
                        "Zaproszenie"
                    );
                }
            }

            // 7. Przekierowujemy klienta z powrotem do widoku listy zaproszeń
            return RedirectToAction("Requests");
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // Zabezpieczenie przed atakami CSRF
        public async Task<IActionResult> RejectRequest()
        {
            // 1. Pobieramy pełne dane zalogowanego klienta
            var user = await _userManager.GetUserAsync(User);

            // 2. Warunek: Upewniamy się, że klient istnieje i rzeczywiście ma oczekujące zaproszenie
            if (user != null && !string.IsNullOrEmpty(user.PendingTrainerId))
            {
                // 3. BEZPIECZEŃSTWO: Zapamiętujemy ID trenera przed usunięciem (ponieważ zaraz ustawimy je na null, a potrzebujemy go do wysłania powiadomienia!)
                var pendingTrenerId = user.PendingTrainerId;

                // 4. Anulujemy zaproszenie – ustawiamy PendingTrenerId na null
                user.PendingTrainerId = null;

                // 5. Zapisujemy zmiany w bazie danych
                await _userManager.UpdateAsync(user);

                // 6. Wysyłamy powiadomienie do trenera, informujące o odrzuceniu zaproszenia
                await _notificationService.SendNotificationAsync(
                    pendingTrenerId,
                    "Zaproszenie odrzucone",
                    $"Klient {user.FirstName} {user.LastName} odrzucił Twoje zaproszenie do współpracy.",
                    "Zaproszenie"
                );
            }
            return RedirectToAction("Requests");
        }

        [HttpGet]
        public async Task<IActionResult> MyTrainer()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

            ApplicationUser? currentTrainer = null;
            if (!string.IsNullOrEmpty(user.TrainerId))
            {
                currentTrainer = await _userManager.FindByIdAsync(user.TrainerId);

                // SELF-HEALING (Naprawa Punktu 3 Audytu 2.0): 
                // Jeśli trener został usunięty z systemu, odpinamy go automatycznie u klienta.
                if (currentTrainer == null)
                {
                    user.TrainerId = null;
                    await _userManager.UpdateAsync(user);
                }
            }

            var history = await _context.CollaborationHistories
                                        .Where(h => h.ClientId == user.Id && !h.IsDeletedByClient)
                                        .OrderByDescending(h => h.EndDate)
                                        .ToListAsync();

            ViewBag.History = history;
            return View(currentTrainer);
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // Zabezpieczenie przed atakami CSRF
        public async Task<IActionResult> DeleteHistoryRecord([FromBody] int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var record = await _context.CollaborationHistories.FindAsync(id);
            if (record != null && record.ClientId == user.Id)
            {
                record.IsDeletedByClient = true;

                // Jeśli oboje usunęli, fizycznie skasuj z bazy
                if (record.IsDeletedByClient && record.IsDeletedByTrainer)
                {
                    _context.CollaborationHistories.Remove(record);
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            return Json(new { success = false });
        }

        public class RemoveTrainerRequestDto
        {
            [StringLength(500, ErrorMessage = "Powód nie może być dłuższy niż 500 znaków.")]
            public string? Reason { get; set; } = string.Empty;
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // Zabezpieczenie przed atakami CSRF
        public async Task<IActionResult> RemoveTrainer([FromBody] RemoveTrainerRequestDto request)
        {
            // LOGIKA ELASTYCZNA (UX): Zezwalamy klientowi na brak podania powodu w oknie dialogowym.
            // Zamiast odrzucać całą operację z kodem HTTP 400 Bad Request, ustawiamy elegancką wartość domyślną.
            var reason = string.IsNullOrWhiteSpace(request?.Reason) ? "Brak podanego powodu." : request.Reason;

            // OCHRONA BAZY DANYCH: Jeśli powód jest zbyt długi (np. powyżej 500 znaków), przycinamy go.
            // Stosujemy zalecaną przez nowsze standardy C# składnię zakresu (Range Operator) `[..500]` zamiast klasycznego Substring.
            if (reason.Length > 500)
            {
                reason = reason[..500];
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null || string.IsNullOrEmpty(user.TrainerId))
            {
                return Json(new { success = false, message = "Błąd. Nie masz przypisanego trenera." });
            }

            // Wywołujemy bezpieczną metodę z serwisu (z obsługą transakcji i weryfikacją klienta - ochrona IDOR)
            var success = await _userService.TerminateCollaborationAsync(user.Id, reason, "Klient", user.Id);

            if (success) return Json(new { success = true });

            return Json(new { success = false, message = "Wystąpił błąd podczas kończenia współpracy." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // Zabezpieczenie przed atakami CSRF
        // Akcja Klienta do samodzielnego planowania treningu.
        // Wykorzystujemy Service Layer, aby odseparować logikę biznesową od kontrolera.
        public async Task<IActionResult> SaveWorkout([FromBody] PlannedWorkout model)
        {
            ModelState.Remove("ClientId");
            ModelState.Remove("Client");

            if (ModelState.IsValid)
            {
                // Używamy ?? string.Empty, aby zapewnić niepuste ID użytkownika, co eliminuje ostrzeżenie CS8604.
                var success = await _workoutService.AddWorkoutAsync(model, _userManager.GetUserId(User) ?? string.Empty, "Klient");
                return Json(new { success, message = success ? "" : "Błąd: Czas rozpoczęcia nie może być w przeszłości." });
            }
            var errors = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return Json(new { success = false, message = errors });
        }

        [HttpGet]
        public async Task<IActionResult> GetWorkouts()
        {
            var userId = _userManager.GetUserId(User);
            var dbWorkouts = await _context.PlannedWorkouts
                                           .Where(w => w.ClientId == userId && w.Status != "Odwołany")
                                           .ToListAsync();

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
        [ValidateAntiForgeryToken] // Zabezpieczenie przed atakami CSRF
        public async Task<IActionResult> DeleteWorkout([FromBody] int id)
        {
            //Zalecam serwisowi usuniecie treningu u danym ID
            //Serwis sprawdzi czy ten trening na pewno należdy do tego Klienta
            var success = await _workoutService.DeleteWorkoutAsync(id, _userManager.GetUserId(User) ?? string.Empty, "Klient");
            return Json(new { success });
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // Zabezpieczenie przed atakami CSRF
        //Metoda do edycji istniejącego treningu
        public async Task<IActionResult> EditWorkout([FromBody] PlannedWorkout model)
        {
            ModelState.Remove("ClientId");
            ModelState.Remove("Client");

            if (!ModelState.IsValid)
            {
                var errors = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return Json(new { success = false, message = errors });
            }
            //Przekazujemy zmieniony model do serwisu
            var success = await _workoutService.UpdateWorkoutAsync(model, _userManager.GetUserId(User) ?? string.Empty, "Klient");
            //Zwracamy wynik. Jesli sie nie udalo , informujemy o możliwych przyczynach.
            return Json(new { success, message = success ? "" : "Błąd: Nie można edytować treningu na czas przeszły lub brak uprawnień." });
        }
    }
}
