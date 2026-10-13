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
        AplikacjaNaSiłownię.Services.INotificationService notificationService,
        Microsoft.AspNetCore.Hosting.IWebHostEnvironment webHostEnvironment) : IUserService
    {
        private readonly UserManager<ApplicationUser> _userManager = userManager;
        private readonly ApplicationDbContext _context = context;
        private readonly ILogger<UserService> _logger = logger;
        private readonly AplikacjaNaSiłownię.Services.INotificationService _notificationService = notificationService;
        private readonly Microsoft.AspNetCore.Hosting.IWebHostEnvironment _webHostEnvironment = webHostEnvironment;

        public async Task<bool> DeleteUserAsync(string userId, string deletedByRole)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return false;

            // Zabezpieczenie przed usunięciem Admina (błyskawiczne sprawdzenie właściwości w pamięci)
            if (user.Role == "Admin") return false;

            // Uproszczone using - transakcja zostanie zakończona (Commit/Rollback) po wyjściu z zakresu metody
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (user.Role == "Trener")
                {
                    var clients = await _userManager.Users.Where(u => u.TrainerId == user.Id || u.PendingTrainerId == user.Id).ToListAsync();
                    foreach (var client in clients)
                    {
                        if (client.TrainerId == user.Id)
                        {
                            _context.CollaborationHistories.Add(new CollaborationHistory
                            {
                                ClientId = client.Id,
                                ClientName = $"{client.FirstName} {client.LastName}",
                                TrainerId = user.Id,
                                TrainerName = $"{user.FirstName} {user.LastName}",
                                EndDate = TimeHelper.GetPolishTime(),
                                EndedByRole = deletedByRole,
                                Reason = "Konto trenera zostało usunięte z systemu."
                            });
                            client.TrainerId = null;
                        }
                        if (client.PendingTrainerId == user.Id) client.PendingTrainerId = null;
                        var updateResult = await _userManager.UpdateAsync(client);
                        // Rzucenie wyjątku wycofa transakcję w razie niepowodzenia aktualizacji powiązanego klienta
                        if (!updateResult.Succeeded) throw new Exception($"Błąd podczas aktualizacji danych klienta {client.Id} przy usuwaniu trenera.");
                    }
                }
                else if (user.Role == "Klient" && !string.IsNullOrEmpty(user.TrainerId))
                {
                    var trainer = await _userManager.FindByIdAsync(user.TrainerId);
                    _context.CollaborationHistories.Add(new CollaborationHistory
                    {
                        ClientId = user.Id,
                        ClientName = $"{user.FirstName} {user.LastName}",
                        TrainerId = user.TrainerId,
                        TrainerName = trainer != null ? $"{trainer.FirstName} {trainer.LastName}" : "Usunięty Trener",
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

                // KROK: Ręczne wyczyszczenie kluczy w historii współpracy (obejście ograniczeń cykliczności/kaskad SQL Server)
                if (_context.Database.IsRelational())
                {
                    await _context.Database.ExecuteSqlRawAsync(
                        "UPDATE CollaborationHistories SET ClientId = NULL WHERE ClientId = {0}", userId);
                    await _context.Database.ExecuteSqlRawAsync(
                        "UPDATE CollaborationHistories SET TrainerId = NULL WHERE TrainerId = {0}", userId);
                }
                else
                {
                    // Dla testów jednostkowych (InMemory), które nie wspierają surowego SQL, wykonujemy aktualizację w pamięci:
                    var histories = await _context.CollaborationHistories
                        .Where(h => h.ClientId == userId || h.TrainerId == userId)
                        .ToListAsync();
                    foreach (var history in histories)
                    {
                        if (history.ClientId == userId) history.ClientId = null;
                        if (history.TrainerId == userId) history.TrainerId = null;
                    }
                }

                await _context.SaveChangesAsync();
                var deleteResult = await _userManager.DeleteAsync(user);
                // Weryfikacja wyniku usuwania w Identity chroni przed zatwierdzeniem niespójnego stanu bazy danych
                if (!deleteResult.Succeeded) throw new Exception($"Błąd podczas usuwania użytkownika {userId} z systemu Identity");
                await transaction.CommitAsync();

                // KROK: Usunięcie fizycznego pliku zdjęcia profilowego z dysku serwera (zapobieganie zaśmiecaniu).
                // Robimy to dopiero PO zatwierdzeniu transakcji - pliku nie da się "wycofać", więc przy rollbacku
                // konto zostałoby w bazie ze ścieżką do nieistniejącego zdjęcia.
                DeleteProfilePictureFile(_webHostEnvironment.WebRootPath, user.ProfilePicturesPath);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wystąpił błąd podczas usuwania użytkownika {UserId}", userId);
                await transaction.RollbackAsync();
                return false;
            }
        }

        private void DeleteProfilePictureFile(string webRootPath, string? fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;

            try
            {
                var filePath = System.IO.Path.Combine(webRootPath, "uploads", "profiles", fileName);
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                // Zmiany w bazie są już zapisane - osierocony plik nie może zmienić wyniku operacji, więc tylko go logujemy.
                _logger.LogWarning(ex, "Nie udało się usunąć pliku zdjęcia profilowego {FileName}", fileName);
            }
        }

        public async Task<bool> ApproveTrainerAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null && user.Role == "Trener")
            {
                user.IsApproved = true;
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
            if (user != null && user.Role == "Trener" && !user.IsApproved)
            {
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

            if (user.Role == "Trener")
            {
                var clientCount = await _userManager.Users.CountAsync(u => u.TrainerId == userId || u.PendingTrainerId == userId);
                if (clientCount > 0)
                {
                    return (true, $"Uwaga! Ten trener posiada obecnie {clientCount} przypisanych klientów. Usunięcie konta spowoduje natychmiastowe zerwanie tych współprac.");
                }
            }
            else if (user.Role == "Klient" && !string.IsNullOrEmpty(user.TrainerId))
            {
                var trainer = await _userManager.FindByIdAsync(user.TrainerId);
                string trainerName = trainer != null ? $"{trainer.FirstName} {trainer.LastName}" : "trenera";
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
            if (client == null || string.IsNullOrEmpty(client.TrainerId)) return false;

            // KROK 1: Weryfikacja uprawnień (Zabezpieczenie przed atakami IDOR).
            // Sprawdzamy, czy osoba żądająca zakończenia współpracy ma do tego prawo względem danego klienta.
            if (endedByRole == "Trener" && client.TrainerId != performingUserId) return false;
            if (endedByRole == "Klient" && client.Id != performingUserId) return false;
            // Admin (Administrator) nie podlega tym restrykcjom.

            var trainer = await _userManager.FindByIdAsync(client.TrainerId);

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
                    ClientName = $"{client.FirstName} {client.LastName}",
                    TrainerId = client.TrainerId,
                    TrainerName = trainer != null ? $"{trainer.FirstName} {trainer.LastName}" : "Usunięty Trener",
                    EndDate = TimeHelper.GetPolishTime(),
                    EndedByRole = endedByRole,
                    Reason = reason
                };
                _context.CollaborationHistories.Add(historyRecord);

                // KROK 4: Aktualizacja danych użytkownika (odpięcie trenera).
                client.TrainerId = null;
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
                        $"Trener {trainer?.FirstName} {trainer?.LastName} zakończył współpracę z Tobą. Powód: {reason}",
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
                            $"Klient {client.FirstName} {client.LastName} zakończył współpracę z Tobą. Powód: {reason}",
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
                            $"Administrator zakończył współpracę między Tobą a klientem {client.FirstName} {client.LastName}. Powód: {reason}",
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

            // 7. BAZA DANYCH: Przypisujemy nową nazwę pliku do profilu i zapisujemy zmiany.
            var oldFileName = user.ProfilePicturesPath;
            user.ProfilePicturesPath = uniqueFileName;
            var result = await _userManager.UpdateAsync(user);

            // 8. SPRZĄTANIE: Plik usuwamy dopiero PO zapisie w bazie (tak samo jak przy usuwaniu konta).
            // Sukces -> znika stare zdjęcie. Błąd -> znika nowo wgrany plik, a profil nadal wskazuje na istniejące stare zdjęcie.
            DeleteProfilePictureFile(webRootPath, result.Succeeded ? oldFileName : uniqueFileName);

            return result.Succeeded;
        }
    }
}
