using AplikacjaNaSiłownię.Models;
using AplikacjaNaSiłownię.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AplikacjaNaSiłownię.Controllers
{
    // Wykorzystujemy Primary Constructor (C# 12) do wstrzykiwania zależności w jednej linii.
    public class AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        Services.IUserService userService,
        IWebHostEnvironment webHostEnvironment,
        Services.INotificationService notificationService) : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager = userManager;
        private readonly SignInManager<ApplicationUser> _signInManager = signInManager;
        private readonly Services.IUserService _userService = userService;
        private readonly IWebHostEnvironment _webHostEnvironment = webHostEnvironment;
        private readonly Services.INotificationService _notificationService = notificationService;

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // Zabezpieczenie przed atakami CSRF
        // Ograniczamy liczbę prób rejestracji (Rate Limiting), aby zapobiec botom tworzącym masowo konta.
        [EnableRateLimiting("LoginPolicy")]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (ModelState.IsValid)
            {
                // BIAŁA LISTA RÓL: Zabezpieczenie serwerowe przed nieuprawnionym przypisaniem roli.
                // Akceptujemy tylko "Trener" i "Klient". Każda inna wartość (np. "Admin" wstrzyknięty przez F12)
                // zostanie natychmiast odrzucona.
                if (model.Role != "Trener" && model.Role != "Klient")
                {
                    ModelState.AddModelError(string.Empty, "Wybrana rola jest nieprawidłowa. Możesz zarejestrować się tylko jako Trener lub Klient.");
                    return View(model);
                }

                // ZABEZPIECZENIE PRZED BŁĘDEM WYŚCIGU (Race Condition):
                // Ustawiamy IsApproved = model.Rola != "Trener" bezpośrednio w inicjalizatorze obiektu.
                // Dzięki temu trener jest tworzony w bazie danych od razu z flagą 'false'. Zapobiega to przejściowemu
                // zapisywaniu trenera z wartością domyślną 'true' w bazie, co mogłoby umożliwić zalogowanie się
                // w czasie pomiędzy CreateAsync a późniejszym UpdateAsync (okno podatności), lub gdyby aplikacja
                // uległa awarii w trakcie trwania metody.
                var user = new ApplicationUser
                {
                    UserName = model.Email,
                    Email = model.Email,
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    Role = model.Role,
                    IsApproved = model.Role != "Trener"
                };

                // TODO: W produkcji użyć silniejszych polityk haseł i dodatkowej walidacji złożoności po stronie serwera
                var result = await _userManager.CreateAsync(user, model.Password);

                if (result.Succeeded)
                {
                    if (model.Role == "Trener")
                    {
                        // Powiadomienie wszystkich administratorów o nowej rejestracji trenera.
                        // Role trzymamy w kolumnie Role (nie w tabeli AspNetUserRoles), dlatego szukamy po tej kolumnie -
                        // GetUsersInRoleAsync zwracałoby tutaj zawsze pustą listę.
                        var adminIds = await _userManager.Users
                            .Where(u => u.Role == "Admin")
                            .Select(u => u.Id)
                            .ToListAsync();
                        foreach (var adminId in adminIds)
                        {
                            await _notificationService.SendNotificationAsync(
                                adminId,
                                "Nowy Trener do weryfikacji",
                                $"Użytkownik {user.FirstName} {user.LastName} ({user.Email}) zarejestrował się jako Trener i oczekuje na zatwierdzenie konta.",
                                "System"
                            );
                        }

                        TempData["RegistrationSuccess"] = "Twoje konto trenera zostało pomyślnie utworzone, jednak wymaga zatwierdzenia przez administratora. Zalogowanie będzie możliwe po akceptacji.";
                        return RedirectToAction("Login", "Account");
                    }

                    await _signInManager.SignInAsync(user, isPersistent: false);
                    return RedirectToAction("Index", "Home");
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return View(model);
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            // Adres, na który użytkownik próbował wejść przed przekierowaniem do logowania.
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // Zabezpieczenie przed atakami CSRF
        // Krytyczne zabezpieczenie: Rate Limiting chroni endpoint logowania przed automatycznymi atakami Brute Force.
        [EnableRateLimiting("LoginPolicy")]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (ModelState.IsValid)
            {
                // Szukamy po adresie e-mail LUB po nazwie użytkownika (UserName)
                var user = await _userManager.FindByEmailAsync(model.Email) ?? await _userManager.FindByNameAsync(model.Email);

                if (user == null)
                {
                    // Zapobieganie User Enumeration (nie zdradzamy czy e-mail istnieje)
                    ModelState.AddModelError(string.Empty, "Nieprawidłowy e-mail lub hasło.");
                    return View(model);
                }

                // Najpierw TYLKO weryfikujemy hasło (bez logowania), z włączonym zabezpieczeniem przed Brute Force
                // (lockoutOnFailure: true). Dzięki temu komunikat o oczekiwaniu na zatwierdzenie widzi wyłącznie
                // osoba znająca hasło - wcześniej zdradzał on istnienie konta każdemu, kto wpisał sam e-mail.
                var result = await _signInManager.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: true);

                if (result.Succeeded)
                {
                    // Blokujemy logowanie TYLKO dla Trenerów, którzy nie są zatwierdzeni.
                    // Klienci i Admini (szczególnie ci założeni przed dodaniem pola IsApproved)
                    // mogą się logować bez przeszkód.
                    if (user.Role == "Trener" && !user.IsApproved)
                    {
                        ModelState.AddModelError(string.Empty, "Konto oczekuje na zatwierdzenie przez administratora. Spróbuj ponownie później.");
                        return View(model);
                    }

                    await _signInManager.SignInAsync(user, isPersistent: false);

                    // Wracamy na stronę, z której użytkownik został odesłany do logowania.
                    // Url.IsLocalUrl chroni przed atakiem Open Redirect (przekierowaniem na obcą domenę).
                    if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    {
                        return LocalRedirect(returnUrl);
                    }

                    return RedirectToAction("Index", "Home");
                }

                if (result.IsLockedOut)
                {
                    ModelState.AddModelError(string.Empty, "To konto zostało tymczasowo zablokowane z powodu zbyt wielu nieudanych prób logowania. Spróbuj ponownie później.");
                    return View(model);
                }

                // Zapobieganie User Enumeration
                ModelState.AddModelError(string.Empty, "Nieprawidłowy e-mail lub hasło.");
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // Zabezpieczenie przed atakami CSRF
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            TempData["SuccessMessage"] = "Wylogowano pomyślnie.";
            return RedirectToAction("Index", "Home");
        }

        // Strona wyświetlana zalogowanemu użytkownikowi, który nie ma uprawnień do zasobu (HTTP 403).
        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Profile()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound("Nie znaleziono użytkownika.");
            }

            if (user.Role == "Klient" && !string.IsNullOrEmpty(user.TrainerId))
            {
                var trener = await _userManager.FindByIdAsync(user.TrainerId);
                if (trener != null)
                {
                    ViewBag.TrenerName = $"{trener.FirstName} {trener.LastName}";
                }
            }

            return View(user);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadProfilePicture(IFormFile profilePicture)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            // Używamy profesjonalnego sposobu pobierania ścieżki do wwwroot
            var webRootPath = _webHostEnvironment.WebRootPath;

            var success = await _userService.UpdateProfilePictureAsync(userId, profilePicture, webRootPath);

            if (success)
            {
                TempData["SuccessMessage"] = "Zdjęcie profilowe zostało zaktualizowane!";
            }
            else
            {
                TempData["ErrorMessage"] = "Błąd podczas przesyłania zdjęcia (zły format lub zbyt duży rozmiar).";
            }

            return RedirectToAction("Profile");
        }

        // Akcja asynchroniczna obsługująca żądanie zmiany hasła wysłane przez AJAX.
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        // Krytyczne zabezpieczenie: Rate Limiting chroni przed próbami masowej zmiany hasła przez boty / złośliwe skrypty.
        [EnableRateLimiting("LoginPolicy")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto model)
        {
            // Bezpieczeństwo: walidacja obecności i poprawności modelu po stronie serwera
            if (model == null || string.IsNullOrWhiteSpace(model.CurrentPassword) || string.IsNullOrWhiteSpace(model.NewPassword))
            {
                return Json(new { success = false, message = "Dane wejściowe nie mogą być puste." });
            }

            // Administrator nie może zmieniać hasła z poziomu profilu (dodatkowe zabezpieczenie).
            if (User.IsInRole("Admin"))
            {
                return Json(new { success = false, message = "Administrator nie może zmieniać hasła z poziomu profilu." });
            }

            // Walidacja zgodności nowych haseł (podstawowe zabezpieczenie UX).
            if (model.NewPassword != model.ConfirmPassword)
            {
                return Json(new { success = false, message = "Nowe hasła nie są identyczne." });
            }

            // Pobranie unikalnego identyfikatora obecnie zalogowanego użytkownika.
            var userId = _userManager.GetUserId(User);

            // Zabezpieczenie przed wygaśnięciem sesji i ostrzeżeniami kompilatora o wartości null.
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "Twoja sesja wygasła. Zaloguj się ponownie." });
            }

            // Wywołanie warstwy serwisu (Service Layer) zgodnie z zasadą separacji odpowiedzialności.
            var result = await _userService.ChangePasswordAsync(userId, model.CurrentPassword, model.NewPassword);

            if (result.Succeeded)
            {
                return Json(new { success = true, message = "Hasło zostało pomyślnie zmienione!" });
            }

            // Jeśli dojdziemy tutaj, to znaczy, że zmiana hasła się NIE udała.
            // Musimy sprawdzić dlaczego i poinformować o tym użytkownika po polsku.

            // Pobieramy pierwszy napotkany błąd z listy błędów zwróconych przez system Identity.
            var error = result.Errors.FirstOrDefault();
            string errorMessage = "Wystąpił błąd podczas zmiany hasła.";

            if (error != null)
            {
                // System Identity zwraca kody błędów. "PasswordMismatch" oznacza, 
                // że użytkownik wpisał złe OBECNE hasło w pierwszym polu.
                if (error.Code == "PasswordMismatch")
                {
                    errorMessage = "Obecne hasło jest nieprawidłowe. Spróbuj ponownie.";
                }
                else
                {
                    // Jeśli wystąpił inny błąd (np. nowe hasło jest zbyt krótkie), 
                    // pobieramy jego opis przygotowany przez system.
                    errorMessage = error.Description;
                }
            }

            return Json(new { success = false, message = errorMessage });
        }
    }
}
