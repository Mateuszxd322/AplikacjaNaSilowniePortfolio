using AplikacjaNaSiłownię.Data;
using AplikacjaNaSiłownię.Helpers;
using AplikacjaNaSiłownię.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AplikacjaNaSiłownię.Controllers
{
    // Ograniczamy dostęp do całego kontrolera wyłącznie do zalogowanych użytkowników z rolami: Admin, Trener lub Klient.
    [Authorize(Roles = "Admin,Trener,Klient")]

    // Wykorzystujemy funkcjonalność C# 12 - Primary Constructor (Główny Konstruktor klasy).
    // Pozwala on wstrzyknąć zależności (DbContext i UserManager) bezpośrednio w definicji klasy bez pisania tradycyjnego konstruktora.
    public class DashboardController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager) : Controller
    {
        // Prywatne, tylko do odczytu pola przechowujące wstrzyknięte instancje usług (Dependency Injection).
        private readonly ApplicationDbContext _context = context;
        private readonly UserManager<ApplicationUser> _userManager = userManager;

        /// <summary>
        /// Główna akcja ładująca widok panelu statystyk.
        /// Przygotowuje listę klientów dla listy rozwijanej (dropdown) na podstawie roli użytkownika.
        /// </summary>
        public async Task<IActionResult> Index()
        {
            // Pobieramy ID obecnie zalogowanego użytkownika za pomocą Identity UserManager.
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            // Przygotowujemy pustą listę obiektów użytkowników (klientów).
            var clients = new List<ApplicationUser>();

            // Scenariusz A: Zalogowany użytkownik to Administrator systemu.
            if (User.IsInRole("Admin"))
            {
                // Admin ma prawo przeglądać statystyki wszystkich klientów zarejestrowanych na siłowni.
                // Wyciągamy z bazy użytkowników o roli "Klient", sortujemy ich alfabetycznie po nazwisku i pobieramy asynchronicznie listę.
                clients = await _context.Users
                    .Where(u => u.Role == "Klient")
                    .OrderBy(u => u.LastName)
                    .ToListAsync();
            }
            // Scenariusz B: Zalogowany użytkownik to Trener.
            else if (User.IsInRole("Trener"))
            {
                // Trener może oglądać statystyki wyłącznie tych klientów, którzy są do niego bezpośrednio przypisani.
                // Filtrujemy po TrenerId równym ID zalogowanego trenera.
                clients = await _context.Users
                    .Where(u => u.TrainerId == userId && u.Role == "Klient")
                    .OrderBy(u => u.LastName)
                    .ToListAsync();
            }

            // Przekazujemy listę klientów do widoku za pomocą dynamicznego obiektu ViewBag,
            // aby zasilić nią dropdown <select> na frontendzie.
            ViewBag.Clients = clients;
            return View();
        }

        /// <summary>
        /// Endpoint API (zwracający JSON) pobierający szczegółowe statystyki jednego wybranego klienta.
        /// Posiada pełną ochronę przed atakiem IDOR (Insecure Direct Object Reference).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetClientStats(string? clientId = null, string timeframe = "month")
        {
            // Pobieramy ID zalogowanego użytkownika.
            var currentUserId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(currentUserId)) return BadRequest();

            // Domyślnie celem statystyk jest sam zalogowany użytkownik (przypadek Klienta oglądającego swój profil).
            string targetClientId = currentUserId;

            // Zabezpieczenie przed IDOR i kontrola uprawnień:
            // Jeśli żądanie pochodzi od Admina lub Trenera, mają oni prawo sprawdzić statystyki innego użytkownika (podanego w parametrze clientId).
            if (User.IsInRole("Admin") || User.IsInRole("Trener"))
            {
                if (!string.IsNullOrEmpty(clientId))
                {
                    // Dodatkowa, krytyczna weryfikacja dla Trenera:
                    // Sprawdzamy w bazie danych, czy klient o wskazanym ID naprawdę ma przypisanego tego zalogowanego trenera.
                    if (User.IsInRole("Trener"))
                    {
                        var isClientOfTrainer = await _context.Users.AnyAsync(u => u.Id == clientId && u.TrainerId == currentUserId && u.Role == "Klient");

                        // Jeśli trener próbuje ręcznie podmienić ID na klienta innego trenera, serwer odrzuci to żądanie (brak dostępu - HTTP 403).
                        if (!isClientOfTrainer) return Forbid();
                    }

                    // Jeśli weryfikacja przeszła pomyślnie, ustawiamy cel zapytań na żądane ID klienta.
                    targetClientId = clientId;
                }
                else
                {
                    // Obsługa startowa panelu (gdy nikt jeszcze nie został wybrany z dropdownu):
                    // Pobieramy pierwszego lepszego klienta należącego do trenera lub z całej siłowni (dla Admina), aby strona nie wystartowała pusta.
                    if (User.IsInRole("Trener"))
                    {
                        var firstClient = await _context.Users.FirstOrDefaultAsync(u => u.TrainerId == currentUserId && u.Role == "Klient");
                        if (firstClient == null) return Json(new { noClients = true });
                        targetClientId = firstClient.Id;
                    }
                    else if (User.IsInRole("Admin"))
                    {
                        var firstClient = await _context.Users.FirstOrDefaultAsync(u => u.Role == "Klient");
                        if (firstClient == null) return Json(new { noClients = true });
                        targetClientId = firstClient.Id;
                    }
                }
            }

            // Pobieramy dane wybranego klienta z tabeli użytkowników.
            var clientUser = await _context.Users.FindAsync(targetClientId);
            if (clientUser == null) return NotFound();

            // 1. ZLICZANIE STATYSTYK TRENINGOWYCH W BAZIE DANYCH (Szybkie zapytania COUNT w bazie):

            // Łączna liczba wszystkich treningów (nieodwołanych i odwołanych) zapisanych dla tego klienta.
            var totalWorkouts = await _context.PlannedWorkouts.CountAsync(w => w.ClientId == targetClientId);

            // Liczba treningów zrealizowanych: status inny niż odwołany oraz data zakończenia (lub rozpoczęcia) jest w przeszłości względem UTC.
            var completedWorkouts = await _context.PlannedWorkouts.CountAsync(w =>
                w.ClientId == targetClientId &&
                w.Status != "Odwołany" &&
                (w.EndDate ?? w.StartDate) < TimeHelper.GetPolishTime());

            // Liczba treningów zaplanowanych w przyszłości (status "Zaplanowany" i data startu >= UTC).
            var plannedWorkouts = await _context.PlannedWorkouts.CountAsync(w =>
                w.ClientId == targetClientId &&
                w.Status == "Zaplanowany" &&
                (w.EndDate ?? w.StartDate) >= TimeHelper.GetPolishTime());

            // Liczba treningów odwołanych przez klienta lub trenera.
            var cancelledWorkouts = await _context.PlannedWorkouts.CountAsync(w =>
                w.ClientId == targetClientId &&
                w.Status == "Odwołany");

            // 2. DANE DO WYKRESU AKTYWNOŚCI W CZASIE (W zależności od wybranego widoku: miesiąc, tydzień, dzień)
            var chartLabels = new List<string>();
            var chartData = new List<int>();

            if (timeframe == "week")
            {
                // Widok Tygodniowy: Ostatnie 6 tygodni
                var today = TimeHelper.GetPolishTime().Date;
                // Znajdujemy poniedziałek bieżącego tygodnia
                int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
                var startOfCurrentWeek = today.AddDays(-1 * diff);
                var sixWeeksAgo = startOfCurrentWeek.AddDays(-35); // 5 tygodni wstecz * 7 dni

                // Pobieramy daty zrealizowanych treningów klienta z bazy od wyznaczonej granicy
                var workouts = await _context.PlannedWorkouts
                    .Where(w => w.ClientId == targetClientId &&
                                w.Status != "Odwołany" &&
                                (w.EndDate ?? w.StartDate) < TimeHelper.GetPolishTime() &&
                                w.StartDate >= sixWeeksAgo)
                    .Select(w => w.StartDate)
                    .ToListAsync();

                // Iterujemy przez 6 tygodni wstecz do dziś, aby wyliczyć przedziały i sformatować etykiety
                for (int i = 5; i >= 0; i--)
                {
                    var weekStart = startOfCurrentWeek.AddDays(-i * 7);
                    var weekEnd = weekStart.AddDays(6);

                    // Piękne formatowanie przedziałów tygodniowych, np. "11.05 - 17.05"
                    var label = $"{weekStart:dd.MM} - {weekEnd:dd.MM}";
                    chartLabels.Add(label);

                    // Zliczamy treningi należące do danego tygodnia
                    int count = workouts.Count(d => d.Date >= weekStart && d.Date <= weekEnd);
                    chartData.Add(count);
                }
            }
            else if (timeframe == "day")
            {
                // Widok Dzienny: Ostatnie 7 dni (wliczając dzisiejszy)
                var sevenDaysAgo = TimeHelper.GetPolishTime().Date.AddDays(-6);

                // Pobieramy daty zrealizowanych treningów
                var workouts = await _context.PlannedWorkouts
                    .Where(w => w.ClientId == targetClientId &&
                                w.Status != "Odwołany" &&
                                (w.EndDate ?? w.StartDate) < TimeHelper.GetPolishTime() &&
                                w.StartDate >= sevenDaysAgo)
                    .Select(w => w.StartDate)
                    .ToListAsync();

                // Iterujemy od 6 dni temu do dzisiaj
                for (int i = 6; i >= 0; i--)
                {
                    var day = TimeHelper.GetPolishTime().Date.AddDays(-i);

                    // Pobieramy trzyliterowy skrót nazwy dnia i formatujemy jako np. "Pn (11.05)"
                    var dayName = day.ToString("ddd", new System.Globalization.CultureInfo("pl-PL"));
                    var label = $"{char.ToUpper(dayName[0])}{dayName[1..]} ({day:dd.MM})";
                    chartLabels.Add(label);

                    // Zliczamy treningi z danego dnia
                    int count = workouts.Count(d => d.Date == day);
                    chartData.Add(count);
                }
            }
            else
            {
                // Domyślny Widok Miesięczny (timeframe == "month"): Ostatnie 6 miesięcy
                var sixMonthsAgo = TimeHelper.GetPolishTime().AddMonths(-5);

                // Pobieramy i agregujemy dane o zrealizowanych treningach bezpośrednio w bazie SQL
                var monthlyData = await _context.PlannedWorkouts
                    .Where(w => w.ClientId == targetClientId &&
                                w.Status != "Odwołany" &&
                                (w.EndDate ?? w.StartDate) < TimeHelper.GetPolishTime() &&
                                w.StartDate >= new DateTime(sixMonthsAgo.Year, sixMonthsAgo.Month, 1))
                    .GroupBy(w => new { w.StartDate.Year, w.StartDate.Month })
                    .Select(g => new
                    {
                        g.Key.Year,
                        g.Key.Month,
                        Count = g.Count()
                    })
                    .ToListAsync();

                // Przygotowujemy etykiety i uzupełniamy ewentualne miesiące bez treningów zerami
                for (int i = 5; i >= 0; i--)
                {
                    var date = TimeHelper.GetPolishTime().AddMonths(-i);
                    var label = date.ToString("MMMM yyyy", new System.Globalization.CultureInfo("pl-PL"));
                    chartLabels.Add(char.ToUpper(label[0]) + label[1..]);

                    var dbRecord = monthlyData.FirstOrDefault(m => m.Year == date.Year && m.Month == date.Month);
                    chartData.Add(dbRecord?.Count ?? 0);
                }
            }

            // Zwracamy obiekt JSON z wszystkimi obliczonymi statystykami gotowy do zmapowania w JS.
            return Json(new
            {
                clientName = $"{clientUser.FirstName} {clientUser.LastName}",
                totalWorkouts,
                completedWorkouts,
                plannedWorkouts,
                cancelledWorkouts,
                chartLabels,
                chartData
            });
        }

        /// <summary>
        /// Endpoint API pobierający statystyki ogólne (zarządcze) trenera.
        /// Pokazuje m.in. globalną aktywność wszystkich podopiecznych i wyłania lidera aktywności.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetTrainerStats()
        {
            // Pobieramy ID zalogowanego trenera.
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return BadRequest();

            // Pobieramy listę wszystkich aktywnych podopiecznych tego trenera.
            var activeClients = await _context.Users
                .Where(u => u.TrainerId == userId && u.Role == "Klient")
                .ToListAsync();

            var totalClients = activeClients.Count;

            // Zabezpieczenie przed brakiem klientów u nowego trenera:
            // Jeśli trener nikogo nie prowadzi, od razu zwracamy bezpieczny, wyzerowany JSON, zapobiegając błędom w kodzie.
            if (totalClients == 0)
            {
                return Json(new
                {
                    totalClients = 0,
                    totalWorkouts = 0,
                    completedWorkouts = 0,
                    plannedWorkouts = 0,
                    cancelledWorkouts = 0,
                    mostActiveClientName = "Brak przypisanych klientów",
                    chartLabels = new List<string>(),
                    chartData = new List<int>()
                });
            }

            // Pobieramy listę samych identyfikatorów (IDs) klientów trenera.
            var clientIds = activeClients.Select(c => c.Id).ToList();

            // Zliczamy łączne statystyki treningowe u wszystkich podopiecznych trenera razem wziętych.
            var totalWorkouts = await _context.PlannedWorkouts.CountAsync(w => clientIds.Contains(w.ClientId));

            var completedWorkouts = await _context.PlannedWorkouts.CountAsync(w =>
                clientIds.Contains(w.ClientId) &&
                w.Status != "Odwołany" &&
                (w.EndDate ?? w.StartDate) < TimeHelper.GetPolishTime());

            var plannedWorkouts = await _context.PlannedWorkouts.CountAsync(w =>
                clientIds.Contains(w.ClientId) &&
                w.Status == "Zaplanowany" &&
                (w.EndDate ?? w.StartDate) >= TimeHelper.GetPolishTime());

            var cancelledWorkouts = await _context.PlannedWorkouts.CountAsync(w =>
                clientIds.Contains(w.ClientId) &&
                w.Status == "Odwołany");

            // 3. LOGIKA WYŁANIANIA NAJBARDZIEJ AKTYWNEGO KLIENTA (Lidera Aktywności):
            // Grupujemy treningi zrealizowane po ID klienta, zliczamy je, sortujemy malejąco i wybieramy pierwszego z góry.
            var mostActiveClientData = await _context.PlannedWorkouts
                .Where(w => clientIds.Contains(w.ClientId) && w.Status != "Odwołany" && (w.EndDate ?? w.StartDate) < TimeHelper.GetPolishTime())
                .GroupBy(w => w.ClientId)
                .Select(g => new
                {
                    ClientId = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .FirstOrDefaultAsync();

            string mostActiveClientName = "Brak";
            if (mostActiveClientData != null)
            {
                // Znajdujemy pełne imię i nazwisko wyłonionego lidera.
                var clientObj = activeClients.FirstOrDefault(c => c.Id == mostActiveClientData.ClientId);
                if (clientObj != null)
                {
                    mostActiveClientName = $"{clientObj.FirstName} {clientObj.LastName} ({mostActiveClientData.Count} treningów)";
                }
            }

            // 4. PRZYGOTOWANIE DANYCH DO WYKRESU SŁUPKOWEGO (Aktywność poszczególnych klientów):
            // Pobieramy liczbę zrealizowanych treningów dla każdego klienta z osobna.
            var workoutsPerClient = await _context.PlannedWorkouts
                .Where(w => clientIds.Contains(w.ClientId) &&
                            w.Status != "Odwołany" &&
                            (w.EndDate ?? w.StartDate) < TimeHelper.GetPolishTime())
                .GroupBy(w => w.ClientId)
                .Select(g => new
                {
                    ClientId = g.Key,
                    Count = g.Count()
                })
                .ToListAsync();

            var clientLabels = new List<string>();
            var clientWorkoutCounts = new List<int>();

            // Mapujemy dane: każdy klient z listy aktywnych dostaje etykietę (imię nazwisko) oraz liczbę treningów.
            foreach (var client in activeClients)
            {
                clientLabels.Add($"{client.FirstName} {client.LastName}");

                var countObj = workoutsPerClient.FirstOrDefault(w => w.ClientId == client.Id);
                clientWorkoutCounts.Add(countObj?.Count ?? 0);
            }

            // Zwracamy spakowany obiekt JSON dla panelu ogólnego trenera.
            return Json(new
            {
                totalClients,
                totalWorkouts,
                completedWorkouts,
                plannedWorkouts,
                cancelledWorkouts,
                mostActiveClientName,
                chartLabels = clientLabels,
                chartData = clientWorkoutCounts
            });
        }
    }
}
