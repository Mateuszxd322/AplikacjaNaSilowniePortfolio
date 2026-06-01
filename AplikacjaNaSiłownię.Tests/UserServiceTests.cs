using AplikacjaNaSiłownię.Data;
using AplikacjaNaSiłownię.Models;
using AplikacjaNaSiłownię.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AplikacjaNaSiłownię.Tests
{
    /// <summary>
    /// Testy dla UserService.
    /// Wykorzystujemy bibliotekę Moq do symulowania działania UserManager z ASP.NET Core Identity.
    /// </summary>
    // === KROK 10.1: W KAŻDYM MIEJSCU, GDZIE TWORZYSZ "new UserService(...)" ===
    // MUSISZ DODAĆ NOWY PARAMETR: "new Mock<INotificationService>().Object"
    // Przykład: new UserService(..., new Mock<INotificationService>().Object);
    public class UserServiceTests
    {
        private static ApplicationDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                // Ignorujemy ostrzeżenie o transakcjach, bo baza In-Memory ich nie wspiera, 
                // a nasz kod produkcyjny ich używa dla bezpieczeństwa.
                .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            return new ApplicationDbContext(options);
        }

        /// <summary>
        /// Tworzy "udawany" (Mock) obiekt UserManager.
        /// UserManager jest skomplikowany, więc Moq pozwala nam udawać jego metody bez konfigurowania całego Identity.
        /// </summary>
        private static Mock<UserManager<ApplicationUser>> MockUserManager()
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            return new Mock<UserManager<ApplicationUser>>(
                store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        }

        private static UserService CreateUserService(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext db,
            INotificationService? notificationService = null,
            Microsoft.AspNetCore.Hosting.IWebHostEnvironment? webHostEnvironment = null)
        {
            var loggerMock = new Mock<Microsoft.Extensions.Logging.ILogger<UserService>>();
            var notificationMock = notificationService ?? new Mock<INotificationService>().Object;
            var webHostEnvMock = webHostEnvironment ?? new Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().Object;
            return new UserService(userManager, db, loggerMock.Object, notificationMock, webHostEnvMock);
        }

        [Fact]
        public async Task TerminateCollaboration_ValidRequest_CreatesHistoryAndClearsId()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            var userManagerMock = MockUserManager();

            var client = new ApplicationUser { Id = "client-1", TrainerId = "trainer-1", FirstName = "Jan", LastName = "Kowalski" };
            var trainer = new ApplicationUser { Id = "trainer-1", FirstName = "Adam", LastName = "Trener" };

            // Ustawiamy co mają zwracać metody udawanego UserManager
            userManagerMock.Setup(m => m.FindByIdAsync("client-1")).ReturnsAsync(client);
            userManagerMock.Setup(m => m.FindByIdAsync("trainer-1")).ReturnsAsync(trainer);
            userManagerMock.Setup(m => m.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);

            var service = CreateUserService(userManagerMock.Object, db);

            // --- ACT ---
            var result = await service.TerminateCollaborationAsync("client-1", "Koniec współpracy", "Trener", "trainer-1");

            // --- ASSERT ---
            Assert.True(result);
            Assert.Null(client.TrainerId); // Relacja powinna zostać zerwana
            Assert.Equal(1, await db.CollaborationHistories.CountAsync(TestContext.Current.CancellationToken)); // Powinien powstać ślad w historii (audit log)
            
            var history = await db.CollaborationHistories.FirstAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Koniec współpracy", history.Reason);
            Assert.Equal("Trener", history.EndedByRole);
        }

        [Fact]
        public async Task TerminateCollaboration_UnauthorizedTrainer_ReturnsFalse()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            var userManagerMock = MockUserManager();

            // Klient należy do Trenera-1
            var client = new ApplicationUser { Id = "client-1", TrainerId = "trainer-1" };
            userManagerMock.Setup(m => m.FindByIdAsync("client-1")).ReturnsAsync(client);

            var service = CreateUserService(userManagerMock.Object, db);

            // --- ACT ---
            // "Zły" trener próbuje odłączyć klienta innego trenera (próba ataku IDOR)
            var result = await service.TerminateCollaborationAsync("client-1", "Atak", "Trener", "trainer-evil");

            // --- ASSERT ---
            Assert.False(result); // System powinien zablokować nieautoryzowaną akcję
            Assert.Equal("trainer-1", client.TrainerId); // Relacja musi pozostać nienaruszona
        }

        [Fact]
        public async Task DeleteUser_AdminUser_ReturnsFalse()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            var userManagerMock = MockUserManager();
            var adminUser = new ApplicationUser { Id = "admin-id", Role = "Admin" };

            userManagerMock.Setup(m => m.FindByIdAsync("admin-id")).ReturnsAsync(adminUser);
            userManagerMock.Setup(m => m.IsInRoleAsync(adminUser, "Admin")).ReturnsAsync(true);

            var service = CreateUserService(userManagerMock.Object, db);

            // --- ACT ---
            var result = await service.DeleteUserAsync("admin-id", "Admin");

            // --- ASSERT ---
            Assert.False(result); // Usunięcie konta Admina przez samego siebie/kogoś innego musi być zablokowane dla bezpieczeństwa
            // Weryfikujemy, że metoda DeleteAsync nigdy nie została wywołana
            userManagerMock.Verify(m => m.DeleteAsync(It.IsAny<ApplicationUser>()), Times.Never);
        }

        [Fact]
        public async Task ApproveTrainer_ValidId_SetsIsApproved()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            var userManagerMock = MockUserManager();
            var trainer = new ApplicationUser { Id = "trainer-id", Role = "Trener", IsApproved = false };

            userManagerMock.Setup(m => m.FindByIdAsync("trainer-id")).ReturnsAsync(trainer);
            userManagerMock.Setup(m => m.UpdateAsync(trainer)).ReturnsAsync(IdentityResult.Success);

            var service = CreateUserService(userManagerMock.Object, db);

            // --- ACT ---
            var result = await service.ApproveTrainerAsync("trainer-id");

            // --- ASSERT ---
            Assert.True(result);
            Assert.True(trainer.IsApproved); // Flaga zatwierdzenia powinna być na true
        }

        [Fact]
        public async Task RejectTrainer_ValidId_DeletesUser()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            var userManagerMock = MockUserManager();
            var trainer = new ApplicationUser { Id = "trainer-candidate", Role = "Trener", IsApproved = false };

            userManagerMock.Setup(m => m.FindByIdAsync("trainer-candidate")).ReturnsAsync(trainer);
            userManagerMock.Setup(m => m.DeleteAsync(trainer)).ReturnsAsync(IdentityResult.Success);

            var service = CreateUserService(userManagerMock.Object, db);

            // --- ACT ---
            var result = await service.RejectTrainerAsync("trainer-candidate");

            // --- ASSERT ---
            Assert.True(result);
            userManagerMock.Verify(m => m.DeleteAsync(trainer), Times.Once);
        }

        [Fact]
        public async Task DeleteUser_TrainerWithClients_ClearsRelationsCreatesHistoryAndDeletesWorkouts()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            var userManagerMock = MockUserManager();

            var trainer = new ApplicationUser { Id = "trainer-1", Role = "Trener", FirstName = "Adam", LastName = "Trener" };
            var client1 = new ApplicationUser { Id = "client-1", TrainerId = "trainer-1", FirstName = "Jan", LastName = "Kowalski", Role = "Klient" };
            var client2 = new ApplicationUser { Id = "client-2", PendingTrainerId = "trainer-1", FirstName = "Ola", LastName = "Nowak", Role = "Klient" };

            // Dodajemy użytkowników bezpośrednio do bazy InMemory, dzięki czemu db.Users posłuży jako działający IQueryable z obsługą IAsyncQueryable
            db.Users.Add(client1);
            db.Users.Add(client2);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            userManagerMock.Setup(m => m.FindByIdAsync("trainer-1")).ReturnsAsync(trainer);
            userManagerMock.Setup(m => m.IsInRoleAsync(trainer, "Admin")).ReturnsAsync(false);
            userManagerMock.Setup(m => m.Users).Returns(db.Users);

            userManagerMock.Setup(m => m.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
            userManagerMock.Setup(m => m.DeleteAsync(trainer)).ReturnsAsync(IdentityResult.Success);

            // Dodajemy treningi, które powinny zostać usunięte
            db.PlannedWorkouts.Add(new PlannedWorkout { Id = 101, ClientId = "trainer-1", StartDate = DateTime.UtcNow.AddDays(1) });
            db.PlannedWorkouts.Add(new PlannedWorkout { Id = 102, ClientId = "client-1", StartDate = DateTime.UtcNow.AddDays(2) });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var service = CreateUserService(userManagerMock.Object, db);

            // --- ACT ---
            var result = await service.DeleteUserAsync("trainer-1", "Admin");

            // --- ASSERT ---
            Assert.True(result);
            Assert.Null(client1.TrainerId); // Relacja powinna zostać zerwana
            Assert.Null(client2.PendingTrainerId); // Pending również wyczyszczone
            
            // Powinien powstać 1 rekord w historii (dla client1, bo był aktywnym klientem)
            Assert.Equal(1, await db.CollaborationHistories.CountAsync(TestContext.Current.CancellationToken));
            var history = await db.CollaborationHistories.FirstAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Konto trenera zostało usunięte z systemu.", history.Reason);

            // Powinny zostać usunięte treningi powiązane z usuniętym użytkownikiem (w tym klienta, ponieważ usunięcie klienta/trenera czyści treningi)
            var remainingWorkouts = await db.PlannedWorkouts.ToListAsync(TestContext.Current.CancellationToken);
            Assert.DoesNotContain(remainingWorkouts, w => w.ClientId == "trainer-1");
        }

        [Fact]
        public async Task DeleteUser_ClientWithTrainer_CreatesHistoryAndDeletesWorkouts()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            var userManagerMock = MockUserManager();

            var client = new ApplicationUser { Id = "client-1", TrainerId = "trainer-1", FirstName = "Jan", LastName = "Kowalski", Role = "Klient" };
            var trainer = new ApplicationUser { Id = "trainer-1", FirstName = "Adam", LastName = "Trener", Role = "Trener" };

            userManagerMock.Setup(m => m.FindByIdAsync("client-1")).ReturnsAsync(client);
            userManagerMock.Setup(m => m.FindByIdAsync("trainer-1")).ReturnsAsync(trainer);
            userManagerMock.Setup(m => m.IsInRoleAsync(client, "Admin")).ReturnsAsync(false);
            userManagerMock.Setup(m => m.DeleteAsync(client)).ReturnsAsync(IdentityResult.Success);

            // Dodajemy treningi klienta, które muszą zniknąć przy usunięciu konta
            db.PlannedWorkouts.Add(new PlannedWorkout { Id = 201, ClientId = "client-1", StartDate = DateTime.UtcNow.AddDays(1) });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var service = CreateUserService(userManagerMock.Object, db);

            // --- ACT ---
            var result = await service.DeleteUserAsync("client-1", "Admin");

            // --- ASSERT ---
            Assert.True(result);
            
            // Zapis historii współpracy
            Assert.Equal(1, await db.CollaborationHistories.CountAsync(TestContext.Current.CancellationToken));
            var history = await db.CollaborationHistories.FirstAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Konto klienta zostało usunięte z systemu.", history.Reason);

            // Treningi klienta powinny zostać wyczyszczone
            Assert.Equal(0, await db.PlannedWorkouts.CountAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task UpdateProfilePicture_ValidPng_SavesFileAndUpdatesDatabase()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            var userManagerMock = MockUserManager();
            var user = new ApplicationUser { Id = "user-1", FirstName = "Jan", LastName = "Kowalski" };

            userManagerMock.Setup(m => m.FindByIdAsync("user-1")).ReturnsAsync(user);
            userManagerMock.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

            var tempDir = Path.Combine(Directory.GetCurrentDirectory(), "temp_test_uploads_" + Guid.NewGuid().ToString());
            
            var fileMock = new Mock<Microsoft.AspNetCore.Http.IFormFile>();
            var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }; // Poprawna sygnatura PNG (magic number)
            
            fileMock.Setup(_ => _.FileName).Returns("avatar.png");
            fileMock.Setup(_ => _.Length).Returns(pngBytes.Length);
            fileMock.Setup(_ => _.OpenReadStream()).Returns(new MemoryStream(pngBytes));
            fileMock.Setup(_ => _.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                    .Callback<Stream, CancellationToken>((stream, token) => stream.Write(pngBytes, 0, pngBytes.Length))
                    .Returns(Task.CompletedTask);

            var service = CreateUserService(userManagerMock.Object, db);

            try
            {
                // --- ACT ---
                var result = await service.UpdateProfilePictureAsync("user-1", fileMock.Object, tempDir);

                // --- ASSERT ---
                Assert.True(result);
                Assert.NotNull(user.ProfilePicturesPath);
                Assert.EndsWith(".png", user.ProfilePicturesPath);
                
                var savedFilePath = Path.Combine(tempDir, "uploads", "profiles", user.ProfilePicturesPath);
                Assert.True(File.Exists(savedFilePath));
            }
            finally
            {
                // Czyszczenie tymczasowego folderu
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public async Task UpdateProfilePicture_FileTooLarge_ReturnsFalse()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            var userManagerMock = MockUserManager();
            var user = new ApplicationUser { Id = "user-1" };

            userManagerMock.Setup(m => m.FindByIdAsync("user-1")).ReturnsAsync(user);

            var fileMock = new Mock<Microsoft.AspNetCore.Http.IFormFile>();
            fileMock.Setup(_ => _.Length).Returns(3 * 1024 * 1024); // 3MB (Przekracza limit 2MB)

            var service = CreateUserService(userManagerMock.Object, db);

            // --- ACT ---
            var result = await service.UpdateProfilePictureAsync("user-1", fileMock.Object, "dummyPath");

            // --- ASSERT ---
            Assert.False(result);
            Assert.Null(user.ProfilePicturesPath);
        }

        [Fact]
        public async Task UpdateProfilePicture_InvalidExtension_ReturnsFalse()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            var userManagerMock = MockUserManager();
            var user = new ApplicationUser { Id = "user-1" };

            userManagerMock.Setup(m => m.FindByIdAsync("user-1")).ReturnsAsync(user);

            var fileMock = new Mock<Microsoft.AspNetCore.Http.IFormFile>();
            fileMock.Setup(_ => _.FileName).Returns("exploit.exe"); // Niedozwolone rozszerzenie!
            fileMock.Setup(_ => _.Length).Returns(500);

            var service = CreateUserService(userManagerMock.Object, db);

            // --- ACT ---
            var result = await service.UpdateProfilePictureAsync("user-1", fileMock.Object, "dummyPath");

            // --- ASSERT ---
            Assert.False(result);
            Assert.Null(user.ProfilePicturesPath);
        }

        [Fact]
        public async Task UpdateProfilePicture_FakeSignature_ReturnsFalse()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            var userManagerMock = MockUserManager();
            var user = new ApplicationUser { Id = "user-1" };

            userManagerMock.Setup(m => m.FindByIdAsync("user-1")).ReturnsAsync(user);

            var fileMock = new Mock<Microsoft.AspNetCore.Http.IFormFile>();
            var fakeBytes = System.Text.Encoding.UTF8.GetBytes("Napisano w pliku tekstowym, ale zapisano jako png.");
            
            fileMock.Setup(_ => _.FileName).Returns("fake_image.png"); // Rozszerzenie poprawne
            fileMock.Setup(_ => _.Length).Returns(fakeBytes.Length);
            fileMock.Setup(_ => _.OpenReadStream()).Returns(new MemoryStream(fakeBytes)); // Ale nagłówek to zwykły tekst (Brak poprawnych Magic Numbers)

            var service = CreateUserService(userManagerMock.Object, db);

            // --- ACT ---
            var result = await service.UpdateProfilePictureAsync("user-1", fileMock.Object, "dummyPath");

            // --- ASSERT ---
            Assert.False(result);
            Assert.Null(user.ProfilePicturesPath);
        }
    }
}
