using AplikacjaNaSiłownię.Data;
using AplikacjaNaSiłownię.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using AplikacjaNaSiłownię.Helpers;
using Microsoft.Extensions.Logging;

namespace AplikacjaNaSiłownię.Services
{
    // Zastosowano Primary Constructor dla serwisów, co eliminuje zbędny kod konstruktora manualnego.

    public class UserService(
        UserManager<ApplicationUser> userManager, 
        ApplicationDbContext context,
        ILogger<UserService> logger,
        AplikacjaNaSiłownię.Services.INotificationService notificationService) : IUserService
    {
        private readonly UserManager<ApplicationUser> _userManager = userManager;
        private readonly ApplicationDbContext _context = context;
        private readonly ILogger<UserService> _logger = logger;
        private readonly AplikacjaNaSiłownię.Services.INotificationService _notificationService = notificationService;

        public async Task<bool> DeleteUserAsync(string userId, string deletedByRole)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return false;

            // Zabezpieczenie przed usunięciem Admina
            if (await _userManager.IsInRoleAsync(user, "Admin")) return false;

            // Uproszczone using - transakcja zostanie zakończona (Commit/Rollback) po wyjściu z zakresu metody
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (user.Rola == "Trener")
                {
                    var clients = await _userManager.Users.Where(u => u.TrenerId == user.Id || u.PendingTrenerId == user.Id).ToListAsync();
                    foreach (var client in clients)
                    {
                        if (client.TrenerId == user.Id)
                        {
                            _context.CollaborationHistories.Add(new CollaborationHistory
                            {
                                ClientId = client.Id,
                                ClientName = $"{client.Imie} {client.Nazwisko}",
                                TrenerId = user.Id,
                                TrenerName = $"{user.Imie} {user.Nazwisko}",
                                EndDate = TimeHelper.GetPolishTime(),
                                EndedByRole = deletedByRole,
                                Reason = "Konto trenera zostało usunięte z systemu."
                            });
                            client.TrenerId = null;
                        }
                        if (client.PendingTrenerId == user.Id) client.PendingTrenerId = null;
                        await _userManager.UpdateAsync(client);
                    }
                }
                else if (user.Rola == "Klient" && !string.IsNullOrEmpty(user.TrenerId))
                {
                    var trainer = await _userManager.FindByIdAsync(user.TrenerId);
                    _context.CollaborationHistories.Add(new CollaborationHistory
                    {
                        ClientId = user.Id,
                        ClientName = $"{user.Imie} {user.Nazwisko}",
                        TrenerId = user.TrenerId,
                        TrenerName = trainer != null ? $"{trainer.Imie} {trainer.Nazwisko}" : "Usunięty Trener",
                        EndDate = TimeHelper.GetPolishTime(),
                        EndedByRole = deletedByRole,
                        Reason = "Konto klienta zostało usunięte z systemu."
                    });
                }

                // KROK: Usunięcie treningów powiązanych z użytkownikiem (Naprawa Punktu 1 Audytu)
                // Musimy to zrobić przed usunięciem użytkownika, aby nie naruszyć kluczy obcych w bazie.
                var workouts = await _context.PlannedWorkouts.Where(w => w.ClientId == userId).ToListAsync();
                if (workouts.Count > 0)
                {
                    _context.PlannedWorkouts.RemoveRange(workouts);
                }

                // KROK: Usunięcie powiadomień powiązanych z użytkownikiem (Zapobieganie osieroconym rekordom).
                // Ponieważ powiadomienia (Notifications) są powiązane z użytkownikiem za pomocą UserId typu string,
                // a nie twardego klucza obcego EF z automatyczną kaskadą, ich nieusunięcie skutkowałoby
                // powstawaniem osieroconych rekordów i zaśmiecaniem bazy danych po skasowaniu użytkownika.
                var notifications = await _context.Notifications.Where(n => n.UserId == userId).ToListAsync();
                if (notifications.Count > 0)
                {
                    _context.Notifications.RemoveRange(notifications);
                }

                await _context.SaveChangesAsync();
                await _userManager.DeleteAsync(user);
                await transaction.CommitAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wystąpił błąd podczas usuwania użytkownika {UserId}", userId);
                await transaction.RollbackAsync();
                return false;
            }
        }

        public async Task<bool> ApproveTrainerAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null && user.Rola == "Trener")
            {
                user.IsApproved = true;
                if (!await _userManager.IsInRoleAsync(user, "Trener"))
                {
                    await _userManager.AddToRoleAsync(user, "Trener");
                }
                var result = await _userManager.UpdateAsync(user);
                
                if (result.Succeeded)
                {
                    await _notificationService.SendNotificationAsync(
                        user.Id,
                        "Konto zatwierdzone!",
                        "Twoje konto trenera zostało pomyślnie zatwierdzone przez administratora. Witamy w systemie!",
                        "System"
                    );
                }

                return result.Succeeded;
            }
            return false;
        }

        public async Task<bool> RejectTrainerAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null && user.Rola == "Trener" && !user.IsApproved)
            {
                // Synchronizacja ról: upewniamy się, że użytkownik nie ma roli trenera, jeśli został odrzucony.
                if (await _userManager.IsInRoleAsync(user, "Trener"))
                {
                    await _userManager.RemoveFromRoleAsync(user, "Trener");
                }

                // W tym systemie odrzucenie prośby o bycie trenerem skutkuje usunięciem konta "kandydata".
                var result = await _userManager.DeleteAsync(user);
                return result.Succeeded;
            }
            return false;
        }

        public async Task<(bool HasDependencies, string Message)> CheckUserDependenciesAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return (false, "");

            if (user.Rola == "Trener")
            {
                var clientCount = await _userManager.Users.CountAsync(u => u.TrenerId == userId || u.PendingTrenerId == userId);
                if (clientCount > 0)
                {
                    return (true, $"Uwaga! Ten trener posiada obecnie {clientCount} przypisanych klientów. Usunięcie konta spowoduje natychmiastowe zerwanie tych współprac.");
                }
            }
            else if (user.Rola == "Klient" && !string.IsNullOrEmpty(user.TrenerId))
            {
                var trainer = await _userManager.FindByIdAsync(user.TrenerId);
                string trainerName = trainer != null ? $"{trainer.Imie} {trainer.Nazwisko}" : "trenera";
                return (true, $"Uwaga! Ten klient jest obecnie przypisany do {trainerName}. Usunięcie konta spowoduje zerwanie tej współpracy.");
            }

            return (false, "");
        }

        // Logika zmiany hasła: weryfikuje użytkownika i używa managera to bezpiecznej aktualizacji.
        public async Task<IdentityResult> ChangePasswordAsync(string userId, string currentPassword, string newPassword)
        {
            // Znajdujemy użytkownika w bazie (zabezpieczenie przed usunięciem konta w międzyczasie).
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return IdentityResult.Failed(new IdentityError { Description = "Użytkownik nie został znaleziony" });
            }

            // Metoda Identity: sprawdza stare hasło, waliduje nowe i zapisuje zmiany w bazie.
            var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
            return result;
        }
        // Metoda kończąca współpracę między trenerem a klientem.
        // Wykorzystuje transakcję oraz weryfikację uprawnień (Security Check), aby zapobiec nieuprawnionym zmianom (IDOR).
        public async Task<bool> TerminateCollaborationAsync(string clientId, string reason, string endedByRole, string performingUserId)
        {
            var client = await _userManager.FindByIdAsync(clientId);
            if (client == null || string.IsNullOrEmpty(client.TrenerId)) return false;

            // KROK 1: Weryfikacja uprawnień (Zabezpieczenie przed atakami IDOR).
            // Sprawdzamy, czy osoba żądająca zakończenia współpracy ma do tego prawo względem danego klienta.
            if (endedByRole == "Trener" && client.TrenerId != performingUserId) return false;
            if (endedByRole == "Klient" && client.Id != performingUserId) return false;
            // Admin (Administrator) nie podlega tym restrykcjom.

            var trainer = await _userManager.FindByIdAsync(client.TrenerId);

            // KROK 2: Rozpoczęcie transakcji bazodanowej.
            // Transakcja gwarantuje atomowość operacji - albo oba kroki (historia i update) zostaną wykonane, albo żaden.
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // KROK 3: Zapisujemy historię współpracy.
                // Dzięki temu nawet po odpięciu trenera, ślad po ich współpracy zostanie w systemie dla celów analitycznych.
                var historyRecord = new CollaborationHistory
                {
                    ClientId = client.Id,
                    ClientName = $"{client.Imie} {client.Nazwisko}",
                    TrenerId = client.TrenerId,
                    TrenerName = trainer != null ? $"{trainer.Imie} {trainer.Nazwisko}" : "Usunięty Trener",
                    EndDate = TimeHelper.GetPolishTime(),
                    EndedByRole = endedByRole,
                    Reason = reason
                };
                _context.CollaborationHistories.Add(historyRecord);

                // KROK 4: Aktualizacja danych użytkownika (odpięcie trenera).
                client.TrenerId = null;
                var updateResult = await _userManager.UpdateAsync(client);

                if (!updateResult.Succeeded) throw new Exception("Błąd podczas aktualizacji IdentityUser.");

                // KROK 5: Zapisanie zmian w kontekście bazy danych.
                await _context.SaveChangesAsync();

                // KROK 6: Zatwierdzenie transakcji (Commit).
                // Dopiero w tym momencie zmiany stają się trwałe w bazie danych.
                await transaction.CommitAsync();


                if (endedByRole == "Trener")
                {
                    await _notificationService.SendNotificationAsync(
                        client.Id,
                        "Koniec współpracy",
                        $"Trener {trainer?.Imie} {trainer?.Nazwisko} zakończył współpracę z Tobą. Powód: {reason}",
                        "KoniecWspolpracy"
                    );
                }
                else if (endedByRole == "Klient")
                {
                    if (trainer != null)
                    {
                        await _notificationService.SendNotificationAsync(
                            trainer.Id,
                            "Koniec współpracy",
                            $"Klient {client.Imie} {client.Nazwisko} zakończył współpracę z Tobą. Powód: {reason}",
                            "KoniecWspolpracy"
                        );
                    }
                }
                else // Admin
                {
                    await _notificationService.SendNotificationAsync(
                        client.Id,
                        "Koniec współpracy",
                        $"Administrator zakończył współpracę między Tobą a trenerem. Powód: {reason}",
                        "KoniecWspolpracy"
                    );
                    if (trainer != null)
                    {
                        await _notificationService.SendNotificationAsync(
                            trainer.Id,
                            "Koniec współpracy",
                            $"Administrator zakończył współpracę między Tobą a klientem {client.Imie} {client.Nazwisko}. Powód: {reason}",
                            "KoniecWspolpracy"
                        );
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wystąpił błąd podczas przerywania współpracy dla klienta {ClientId}", clientId);
                // W razie dowolnego błędu wycofujemy wszystkie zmiany (Rollback), chroniąc spójność danych.
                await transaction.RollbackAsync();
                return false;
            }
        }

        public async Task<bool> UpdateProfilePictureAsync(string userId, IFormFile file, string webRootPath)
        {
            // 1. Znajdujemy użytkownika w bazie i sprawdzamy, czy plik został przesłany.
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null || file == null || file.Length == 0) return false;

            // 2. BEZPIECZEŃSTWO: Ograniczamy rozmiar pliku do 2MB (ochrona dysku serwera).
            if (file.Length > 2 * 1024 * 1024) return false;

            // 3. BEZPIECZEŃSTWO: Akceptujemy tylko konkretne rozszerzenia obrazów (ochrona przed złośliwym kodem).
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension)) return false;

            // 3b. BEZPIECZEŃSTWO: Weryfikacja sygnatury pliku (Magic Numbers) w celu ochrony przed Polyglot Upload.
            using (var stream = file.OpenReadStream())
            {
                var buffer = new byte[4];
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, 4));
                if (bytesRead < 3) return false; // JPEG wymaga min. 3 bajtów, PNG min. 4 bajtów
                
                bool isPng = bytesRead == 4 && buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47;
                bool isJpeg = bytesRead >= 3 && buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF;
                
                if (!isPng && !isJpeg) return false;
            }

            // 4. UNIKALNOŚĆ: Generujemy nową nazwę pliku (GUID), aby uniknąć konfliktów nazw na serwerze.
            var uniqueFileName = $"{Guid.NewGuid()}{extension}";
            var uploadsFolder = Path.Combine(webRootPath, "uploads", "profiles");

            // 5. INFRASTRUKTURA: Upewniamy się, że folder docelowy istnieje na dysku.
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            // 6. ZAPIS: Kopiujemy zawartość przesłanego pliku do nowej lokalizacji na serwerze.
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // 7. SPRZĄTANIE: Usuwamy poprzednie zdjęcie użytkownika, aby nie marnować miejsca.
            if (!string.IsNullOrEmpty(user.ProfilePicturesPath))
            {
                var oldPath = Path.Combine(webRootPath, "uploads", "profiles", user.ProfilePicturesPath);
                if (File.Exists(oldPath)) File.Delete(oldPath);
            }

            // 8. BAZA DANYCH: Przypisujemy nową nazwę pliku do profilu i zapisujemy zmiany.
            user.ProfilePicturesPath = uniqueFileName;
            var result = await _userManager.UpdateAsync(user);

            return result.Succeeded;
        }
    }
}
