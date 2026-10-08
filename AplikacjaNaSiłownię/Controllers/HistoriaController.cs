using AplikacjaNaSiłownię.Data;
using AplikacjaNaSiłownię.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AplikacjaNaSiłownię.Controllers
{
    [Authorize]
    // Zastosowano Primary Constructor, aby uprościć kod kontrolera i wstrzykiwanie zależności.
    public class HistoriaController(ApplicationDbContext context, UserManager<ApplicationUser> userManager) : Controller
    {
        private readonly ApplicationDbContext _context = context;
        private readonly UserManager<ApplicationUser> _userManager = userManager;

        private const int PageSize = 50;

        public async Task<IActionResult> Index(int page = 1)
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null) return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            var rola = user.Role;
            ViewBag.UserRole = rola;

            IQueryable<PlannedWorkout> query = _context.PlannedWorkouts.Include(w => w.Client);

            if (rola == "Admin")
            {
                // Widzi wszystko
            }
            else if (rola == "Trener")
            {
                // Widzi treningi swoich przypisanych klientów
                query = query.Where(w => w.Client != null && w.Client.TrainerId == userId);
            }
            else if (rola == "Klient")
            {
                // Widzi tylko swoje
                query = query.Where(w => w.ClientId == userId);
            }

            // STRONICOWANIE: administrator widzi treningi wszystkich klientów, więc pobieramy tylko jedną stronę naraz.
            var totalPages = Math.Max(1, (int)Math.Ceiling(await query.CountAsync() / (double)PageSize));
            page = Math.Clamp(page, 1, totalPages);
            ViewBag.Page = page;
            ViewBag.TotalPages = totalPages;

            // Pobieramy i sortujemy od najnowszych
            var workouts = await query
                .OrderByDescending(w => w.StartDate).ThenByDescending(w => w.Id)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            return View(workouts);
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // Zabezpieczenie przed atakami CSRF
        public async Task<IActionResult> DeleteHistoryRecord([FromBody] int id)
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null) return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return Unauthorized();

            var workout = await _context.PlannedWorkouts.Include(w => w.Client).FirstOrDefaultAsync(w => w.Id == id);
            if (workout == null) return Json(new { success = false, message = "Nie znaleziono treningu." });

            // Historia dotyczy treningów zakończonych lub odwołanych. Nadchodzący trening odwołuje się w kalendarzu
            // (z powiadomieniem drugiej strony), a nie kasuje po cichu z bazy.
            if (workout.GetFinalStatus() == "Zaplanowany")
            {
                return Json(new { success = false, message = "Zaplanowanego treningu nie można usunąć z historii. Odwołaj go w kalendarzu." });
            }

            bool canDelete = false;

            if (user.Role == "Admin")
            {
                canDelete = true;
            }
            else if (user.Role == "Trener")
            {
                if (workout.Client != null && workout.Client.TrainerId == userId)
                {
                    canDelete = true;
                }
            }
            else if (user.Role == "Klient")
            {
                if (workout.ClientId == userId)
                {
                    canDelete = true;
                }
            }

            if (canDelete)
            {
                _context.PlannedWorkouts.Remove(workout);
                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }

            return Json(new { success = false, message = "Brak uprawnień do usunięcia tego rekordu." });
        }
    }
}
