using AplikacjaNaSiłownię.Data;
using AplikacjaNaSiłownię.Helpers;
using AplikacjaNaSiłownię.Models;
using AplikacjaNaSiłownię.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace AplikacjaNaSiłownię.Controllers
{
    [Authorize(Roles = "Admin")]
    // Zastosowano Primary Constructor (C# 12), co pozwala na bezpośrednie przypisanie serwisów do pól bez osobnego konstruktora.
    public class AdminController(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context,
        Services.IUserService userService,
        Services.IWorkoutService workoutService) : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager = userManager;
        private readonly ApplicationDbContext _context = context;
        private readonly Services.IUserService _userService = userService;
        private readonly Services.IWorkoutService _workoutService = workoutService;

        [HttpGet]
        public async Task<IActionResult> Calendar()
        {
            // OPTYMALIZACJA EF CORE: Używamy IQueryable (filtrowanie .Where() przed .ToListAsync()), 
            // aby zapytanie SQL zostało sformatowane i wykonane po stronie bazy danych. 
            // Dzięki temu serwer nie pobiera wszystkich rekordów do pamięci RAM, co oszczędza zasoby.
            var targetUsers = await _userManager.Users
                .Where(u => u.Role == "Klient" || (u.Role == "Trener" && u.IsApproved))
                .OrderBy(u => u.Role)
                .Select(u => new { u.Id, Text = $"{u.Role.ToUpper()}: {u.FirstName} {u.LastName}" })
                .ToListAsync();

            ViewBag.UserList = targetUsers;
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetUserWorkouts(string userId)
        {
            // Optymalizacja: używamy Array.Empty<object>() zamiast new object[] { }, aby uniknąć zbędnych alokacji pamięci.
            // Zwracamy pustą tablicę JSON, jeśli nie przekazano identyfikatora użytkownika.
            if (string.IsNullOrEmpty(userId)) return Json(Array.Empty<object>());

            // Wyszukujemy użytkownika w bazie na podstawie identyfikatora.
            var user = await _userManager.FindByIdAsync(userId);
            // Zabezpieczenie: jeśli taki użytkownik nie istnieje, również zwracamy pustą tablicę.
            if (user == null) return Json(Array.Empty<object>());

            List<PlannedWorkout> dbWorkouts;

            // Logika pobierania treningów zależy od roli użytkownika.
            if (user.Role == "Trener")
            {
                // Dla trenera: pobieramy treningi wszystkich jego przypisanych klientów 
                // (gdzie TrenerId klienta odpowiada ID obecnego trenera).
                // Pomijamy treningi odwołane i dołączamy dane klienta za pomocą .Include().
                dbWorkouts = await _context.PlannedWorkouts
                    .Include(w => w.Client)
                    .Where(w => w.Client != null && w.Client.TrainerId == userId && w.Status != "Odwołany")
                    .ToListAsync();
            }
            else
            {
                // Dla klienta: pobieramy wyłącznie jego własne treningi.
                // Również tutaj pomijamy te o statusie "Odwołany".
                dbWorkouts = await _context.PlannedWorkouts
                    .Where(w => w.ClientId == userId && w.Status != "Odwołany")
                    .ToListAsync();
            }

            // Mapujemy pobrane modele bazodanowe na obiekty anonimowe w kształcie 
            // oczekiwanym przez frontend (np. przez bibliotekę kalendarza - FullCalendar).
            var workouts = dbWorkouts.Select(w =>
            {
                // Trener widzi przed tytułem zajęć imię i nazwisko danego klienta, klient widzi tylko tytuł.
                string evTitle = user.Role == "Trener" ? $"[{w.Client!.FirstName} {w.Client.LastName}] {w.Title}" : w.Title;

                return new
                {
                    id = w.Id,
                    title = evTitle,
                    start = w.StartDate.ToString("yyyy-MM-ddTHH:mm:ss"), // Formatowanie daty kompatybilne m.in. z FullCalendar
                    end = w.EndDate.HasValue ? w.EndDate.Value.ToString("yyyy-MM-ddTHH:mm:ss") : null,
                    description = w.Description,
                    status = w.GetFinalStatus(), // Wykorzystujemy scentralizowaną logikę z modelu
                    className = w.GetCssClass() // Wykorzystujemy scentralizowaną logikę z modelu
                };
            }).ToList();

            // Zwracamy gotową listę jako odpowiedź w formacie JSON
            return Json(workouts);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteWorkout([FromBody] int id)
        {
            // Dodajemy ?? "" ponieważ serwis oczekuje stringa, a GetUserId może technicznie zwrócić null
            var success = await _workoutService.DeleteWorkoutAsync(id, _userManager.GetUserId(User) ?? string.Empty, "Admin");
            return Json(new { success, message = success ? "" : "Błąd: Nie można usunąć treningu." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        // Akcja Administratora pozwalająca na edycję dowolnego treningu w systemie.
        // Wykorzystujemy IWorkoutService z flagą roli "Admin", co daje pełne uprawnienia.
        public async Task<IActionResult> EditWorkout([FromBody] WorkoutDto model)
        {
            if (!ModelState.IsValid)
            {
                var errors = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return Json(new { success = false, message = errors });
            }
            // Dodajemy ?? "" dla bezpieczeństwa typów, mimo że [Authorize] pilnuje zalogowania
            var success = await _workoutService.UpdateWorkoutAsync(model.ToEntity(includeId: true), _userManager.GetUserId(User) ?? string.Empty, "Admin");
            return Json(new { success, message = success ? "" : "Błąd: Nie można edytować treningu odwołanego lub przenieść go w przeszłość." });
        }

        private const int UsersPageSize = 50;

        public async Task<IActionResult> Index(int page = 1)
        {
            // OPTYMALIZACJA: Zamiast pobierać całą listę i filtrować ją w C# (.Where na liście),
            // wysyłamy konkretne zapytanie do bazy danych. To kluczowe przy dużej liczbie użytkowników.
            var query = _userManager.Users.Where(u => u.Role != "Trener" || u.IsApproved);

            // STRONICOWANIE: pobieramy z bazy tylko jedną stronę wyników (Skip/Take wykonuje się po stronie SQL).
            var totalPages = Math.Max(1, (int)Math.Ceiling(await query.CountAsync() / (double)UsersPageSize));
            page = Math.Clamp(page, 1, totalPages);

            var activeUsers = await query
                .OrderBy(u => u.LastName).ThenBy(u => u.FirstName).ThenBy(u => u.Id)
                .Skip((page - 1) * UsersPageSize)
                .Take(UsersPageSize)
                .ToListAsync();

            ViewBag.Page = page;
            ViewBag.TotalPages = totalPages;
            return View(activeUsers);
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var success = await _userService.DeleteUserAsync(userId, "Administrator");

            // Informujemy administratora o wyniku - bez tego nieudane usunięcie wyglądało tak samo jak udane.
            if (success)
            {
                TempData["SuccessMessage"] = "Konto użytkownika zostało usunięte.";
            }
            else
            {
                TempData["ErrorMessage"] = "Nie udało się usunąć konta użytkownika.";
            }

            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> Requests()
        {
            // OPTYMALIZACJA: Filtrowanie po stronie SQL. 
            // .ToListAsync() jest na samym końcu, co oznacza, że tylko przefiltrowane dane trafiają do aplikacji.
            var pendingTrainers = await _userManager.Users
                .Where(u => u.Role == "Trener" && !u.IsApproved)
                .ToListAsync();

            return View(pendingTrainers);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveUser(string userId)
        {
            await _userService.ApproveTrainerAsync(userId);
            return RedirectToAction("Requests");
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // Zabezpieczenie przed atakami CSRF
        public async Task<IActionResult> RejectUser(string userId)
        {
            // Przeniesienie logiki do serwisu (Naprawa Punktu 3 Audytu)
            await _userService.RejectTrainerAsync(userId);
            return RedirectToAction("Requests");
        }

        [HttpGet]
        public async Task<IActionResult> CheckUserDependencies(string userId)
        {
            // Dekonstrukcja krotki (HasDependencies, Message) dla czytelniejszego kodu
            var (hasDependencies, message) = await _userService.CheckUserDependenciesAsync(userId);
            return Json(new { hasDependencies, message });
        }

        public class AdminCollaborationViewModel
        {
            // Inicjalizujemy stringi jako Empty, aby uniknąć ostrzeżeń o braku wartości w konstruktorze
            public string ClientId { get; set; } = string.Empty;
            public string ClientName { get; set; } = string.Empty;
            public string TrainerId { get; set; } = string.Empty;
            public string TrainerName { get; set; } = string.Empty;
        }

        public class TerminateCollaborationDto
        {
            public string ClientId { get; set; } = string.Empty;

            [StringLength(500, ErrorMessage = "Powód nie może być dłuższy niż 500 znaków.")]
            public string Reason { get; set; } = string.Empty;
        }

        [HttpGet]
        public async Task<IActionResult> Collaborations()
        {
            // OPTYMALIZACJA: Pobieramy tylko tych klientów, którzy faktycznie mają trenera.
            var clientsWithTrainers = await _userManager.Users
                .Where(u => u.Role == "Klient" && !string.IsNullOrEmpty(u.TrainerId))
                .ToListAsync();

            // Szybkie pobranie samych trenerów do mapowania nazw.
            var trainers = await _userManager.Users
                .Where(u => u.Role == "Trener")
                .ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}");

            var collaborations = new List<AdminCollaborationViewModel>();

            foreach (var client in clientsWithTrainers)
            {
                // TryGetValue jest wydajniejsze, bo przeszukuje słownik tylko raz zamiast dwóch
                if (!trainers.TryGetValue(client.TrainerId!, out var trainerName))
                {
                    trainerName = "Nieznany Trener";
                }

                collaborations.Add(new AdminCollaborationViewModel
                {
                    ClientId = client.Id,
                    ClientName = $"{client.FirstName} {client.LastName}",
                    // Używamy ?? "" aby upewnić się, że nie przypiszemy nulla do pola typu string
                    TrainerId = client.TrainerId ?? string.Empty,
                    TrainerName = trainerName
                });
            }

            return View(collaborations);
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // Zabezpieczenie przed atakami CSRF
        public async Task<IActionResult> TerminateCollaboration([FromBody] TerminateCollaborationDto request)
        {
            // Walidacja ModelState zapewnia, że dane wejściowe spełniają limity zdefiniowane w DTO.
            if (!ModelState.IsValid || string.IsNullOrEmpty(request?.ClientId)) return BadRequest(ModelState);

            string finalReason = string.IsNullOrWhiteSpace(request.Reason)
                ? "Zakończono przymusowo przez Administratora Systemu z powodów regulaminowych."
                : request.Reason;

            // Delegujemy logikę do serwisu, przekazując ID aktualnego użytkownika dla celów audytowych.
            var success = await _userService.TerminateCollaborationAsync(request.ClientId, finalReason, "Administrator", _userManager.GetUserId(User) ?? "");

            if (success) return Json(new { success = true });

            return Json(new { success = false, message = "Nie odnaleziono aktywnej współpracy dla tego klienta lub wystąpił błąd serwera." });
        }
    }
}
