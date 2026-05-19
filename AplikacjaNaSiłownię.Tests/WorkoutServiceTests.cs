using AplikacjaNaSiłownię.Data;
using AplikacjaNaSiłownię.Models;
using AplikacjaNaSiłownię.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Moq;

namespace AplikacjaNaSiłownię.Tests
{
    /// <summary>
    /// Klasa testowa dla WorkoutService.
    /// Testy jednostkowe sprawdzają czy logika biznesowa (walidacja dat, uprawnienia ról) działa poprawnie.
    /// </summary>
    // === KROK 10.2: W KAŻDYM MIEJSCU, GDZIE TWORZYSZ "new WorkoutService(...)" ===
    // MUSISZ DODAĆ NOWY PARAMETR: "new Mock<INotificationService>().Object"
    // Przykład: new WorkoutService(db, new Mock<INotificationService>().Object);
    public class WorkoutServiceTests
    {
        /// <summary>
        /// Metoda pomocnicza tworząca "udawaną" bazę danych w pamięci RAM.
        /// Dzięki temu testy są szybkie i nie niszczą Twojej prawdziwej bazy danych SQL.
        /// </summary>
        private static ApplicationDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options);
        }

        [Fact] // Atrybut [Fact] oznacza, że ta metoda jest pojedynczym testem jednostkowym.
        public async Task AddWorkout_FutureDate_ReturnsTrue()
        {
            // --- ARRANGE (Przygotowanie) ---
            // Tworzymy bazę danych i dodajemy do niej niezbędne dane (użytkownika).
            var db = GetInMemoryDbContext();
            db.Users.Add(new ApplicationUser { Id = "client-1", TrenerId = "trainer-1", Rola = "Klient" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var service = new WorkoutService(db, new Mock<INotificationService>().Object);
            var workout = new PlannedWorkout 
            { 
                Title = "Test Workout",
                StartDate = DateTime.UtcNow.AddDays(1), // Data jutrzejsza (poprawna)
                ClientId = "client-1"
            };

            // --- ACT (Działanie) ---
            // Wywołujemy metodę, którą chcemy przetestować.
            var result = await service.AddWorkoutAsync(workout, "trainer-1", "Trener");

            // --- ASSERT (Weryfikacja) ---
            // Sprawdzamy czy wynik jest taki, jakiego się spodziewaliśmy.
            Assert.True(result); // Powinno zwrócić true
            Assert.Equal(1, await db.PlannedWorkouts.CountAsync(TestContext.Current.CancellationToken)); // W bazie powinien być 1 trening
        }

        [Fact]
        public async Task AddWorkout_PastDate_ReturnsFalse()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            var service = new WorkoutService(db, new Mock<INotificationService>().Object);
            var workout = new PlannedWorkout 
            { 
                Title = "Old Workout",
                StartDate = DateTime.UtcNow.AddDays(-1), // Data wczorajsza (błędna)
                ClientId = "client-1"
            };

            // --- ACT ---
            var result = await service.AddWorkoutAsync(workout, "trainer-1", "Trener");

            // --- ASSERT ---
            // Serwis powinien zablokować dodanie treningu wstecz i zwrócić false.
            Assert.False(result);
        }

        [Fact]
        public async Task AddWorkout_TrainerAddsToOtherTrainerClient_ReturnsFalse()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            // Klient należy do Trenera-2
            db.Users.Add(new ApplicationUser { Id = "client-1", TrenerId = "trainer-2", Rola = "Klient" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var service = new WorkoutService(db, new Mock<INotificationService>().Object);
            var workout = new PlannedWorkout 
            { 
                ClientId = "client-1",
                StartDate = DateTime.UtcNow.AddDays(1)
            };

            // --- ACT ---
            // Trener-1 próbuje dodać trening dla klienta, który nie jest mu przypisany (próba włamania/oszustwa).
            var result = await service.AddWorkoutAsync(workout, "trainer-1", "Trener");

            // --- ASSERT ---
            Assert.False(result); // Zabezpieczenie IDOR powinno zadziałać.
        }

        [Fact]
        public async Task AddWorkout_KlientAddsToSelf_SetsCorrectClientId()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            var service = new WorkoutService(db, new Mock<INotificationService>().Object);
            var workout = new PlannedWorkout 
            { 
                ClientId = "wrong-id", // Klient próbuje "podrobić" ID w formularzu
                StartDate = DateTime.UtcNow.AddDays(1)
            };

            // --- ACT ---
            // Wywołujemy dodawanie jako Klient z ID "real-klient-id".
            var result = await service.AddWorkoutAsync(workout, "real-klient-id", "Klient");

            // --- ASSERT ---
            Assert.True(result);
            var savedWorkout = await db.PlannedWorkouts.FirstAsync(TestContext.Current.CancellationToken);
            // Serwis powinien zignorować "wrong-id" i ustawić ID osoby zalogowanej.
            Assert.Equal("real-klient-id", savedWorkout.ClientId);
        }

        [Fact]
        public async Task DeleteWorkout_ValidOwner_SetsStatusToOdwołany()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            // Dodajemy użytkownika, aby nawigacja Include(w => w.Client) nie wyrzuciła błędu.
            db.Users.Add(new ApplicationUser { Id = "client-1", Rola = "Klient" });
            
            var workout = new PlannedWorkout 
            { 
                ClientId = "client-1",
                Status = "Zaplanowany",
                StartDate = DateTime.UtcNow.AddDays(1),
                Title = "Test"
            };
            db.PlannedWorkouts.Add(workout);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var generatedId = workout.Id; // Pobieramy ID nadane przez bazę
            var service = new WorkoutService(db, new Mock<INotificationService>().Object);

            // --- ACT ---
            var result = await service.DeleteWorkoutAsync(generatedId, "client-1", "Klient");

            // --- ASSERT ---
            Assert.True(result);
            var updated = await db.PlannedWorkouts.FindAsync([generatedId], TestContext.Current.CancellationToken);
            // Trening nie powinien zostać usunięty z bazy, a jedynie oznaczony jako "Odwołany".
            Assert.Equal("Odwołany", updated?.Status);
        }

        [Fact]
        public async Task UpdateWorkout_UnauthorizedTrainer_ReturnsFalse()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            db.Users.Add(new ApplicationUser { Id = "client-1", TrenerId = "trainer-assigned", Rola = "Klient" });
            db.PlannedWorkouts.Add(new PlannedWorkout 
            { 
                ClientId = "client-1",
                StartDate = DateTime.UtcNow.AddDays(1),
                Title = "Test"
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var generatedId = (await db.PlannedWorkouts.FirstAsync(TestContext.Current.CancellationToken)).Id;
            var service = new WorkoutService(db, new Mock<INotificationService>().Object);

            // --- ACT ---
            // "Zły" trener próbuje zmienić trening klienta, który ma już innego trenera.
            var model = new PlannedWorkout { Id = generatedId, Title = "Hacked", StartDate = DateTime.UtcNow.AddDays(2) };
            var result = await service.UpdateWorkoutAsync(model, "trainer-evil", "Trener");

            // --- ASSERT ---
            Assert.False(result); // Próba nieautoryzowanej zmiany powinna zostać zablokowana.
        }

        [Fact]
        public async Task AddWorkout_EndDateBeforeStartDate_ReturnsFalse()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            db.Users.Add(new ApplicationUser { Id = "client-1", TrenerId = "trainer-1", Rola = "Klient" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var service = new WorkoutService(db, new Mock<INotificationService>().Object);
            var workout = new PlannedWorkout 
            { 
                Title = "Invalid Dates",
                StartDate = DateTime.UtcNow.AddDays(1),
                EndDate = DateTime.UtcNow.AddDays(1).AddHours(-1), // End date BEFORE start date!
                ClientId = "client-1"
            };

            // --- ACT ---
            var result = await service.AddWorkoutAsync(workout, "trainer-1", "Trener");

            // --- ASSERT ---
            Assert.False(result);
            Assert.Equal(0, await db.PlannedWorkouts.CountAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task UpdateWorkout_EndDateBeforeStartDate_ReturnsFalse()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            db.Users.Add(new ApplicationUser { Id = "client-1", TrenerId = "trainer-1", Rola = "Klient" });
            var workout = new PlannedWorkout 
            { 
                ClientId = "client-1",
                StartDate = DateTime.UtcNow.AddDays(1),
                EndDate = DateTime.UtcNow.AddDays(1).AddHours(1),
                Title = "Original Title"
            };
            db.PlannedWorkouts.Add(workout);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var service = new WorkoutService(db, new Mock<INotificationService>().Object);
            var model = new PlannedWorkout 
            { 
                Id = workout.Id,
                Title = "Updated Title",
                StartDate = DateTime.UtcNow.AddDays(1),
                EndDate = DateTime.UtcNow.AddDays(1).AddHours(-2) // Invalid!
            };

            // --- ACT ---
            var result = await service.UpdateWorkoutAsync(model, "trainer-1", "Trener");

            // --- ASSERT ---
            Assert.False(result);
            var saved = await db.PlannedWorkouts.FirstAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Original Title", saved.Title); // Title should not be changed
        }

        [Fact]
        public async Task UpdateWorkout_PastDate_ReturnsFalse()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            db.Users.Add(new ApplicationUser { Id = "client-1", TrenerId = "trainer-1", Rola = "Klient" });
            var workout = new PlannedWorkout 
            { 
                ClientId = "client-1",
                StartDate = DateTime.UtcNow.AddDays(1),
                Title = "Future Workout"
            };
            db.PlannedWorkouts.Add(workout);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var service = new WorkoutService(db, new Mock<INotificationService>().Object);
            var model = new PlannedWorkout 
            { 
                Id = workout.Id,
                Title = "Moved to Past",
                StartDate = DateTime.UtcNow.AddDays(-2) // Move to past (Invalid!)
            };

            // --- ACT ---
            var result = await service.UpdateWorkoutAsync(model, "trainer-1", "Trener");

            // --- ASSERT ---
            Assert.False(result);
            var saved = await db.PlannedWorkouts.FirstAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Future Workout", saved.Title); // Should not change
        }

        [Fact]
        public async Task DeleteWorkout_UnauthorizedTrainer_ReturnsFalse()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            db.Users.Add(new ApplicationUser { Id = "client-1", TrenerId = "trainer-assigned", Rola = "Klient" });
            var workout = new PlannedWorkout 
            { 
                ClientId = "client-1",
                StartDate = DateTime.UtcNow.AddDays(1),
                Status = "Zaplanowany"
            };
            db.PlannedWorkouts.Add(workout);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var service = new WorkoutService(db, new Mock<INotificationService>().Object);

            // --- ACT ---
            // "Zły" trener (trainer-evil) próbuje odwołać/usunąć trening przypisany do innego trenera
            var result = await service.DeleteWorkoutAsync(workout.Id, "trainer-evil", "Trener");

            // --- ASSERT ---
            Assert.False(result);
            var saved = await db.PlannedWorkouts.FirstAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Zaplanowany", saved.Status); // Status must remain "Zaplanowany"
        }

        [Fact]
        public async Task DeleteWorkout_UnauthorizedClient_ReturnsFalse()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            db.Users.Add(new ApplicationUser { Id = "client-owner", Rola = "Klient" });
            var workout = new PlannedWorkout 
            { 
                ClientId = "client-owner",
                StartDate = DateTime.UtcNow.AddDays(1),
                Status = "Zaplanowany"
            };
            db.PlannedWorkouts.Add(workout);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var service = new WorkoutService(db, new Mock<INotificationService>().Object);

            // --- ACT ---
            // "Inny" klient (client-hacker) próbuje usunąć/odwołać cudzy trening
            var result = await service.DeleteWorkoutAsync(workout.Id, "client-hacker", "Klient");

            // --- ASSERT ---
            Assert.False(result);
            var saved = await db.PlannedWorkouts.FirstAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Zaplanowany", saved.Status); // Status must remain "Zaplanowany"
        }

        [Fact]
        public async Task GetClientWorkouts_ExcludesCancelledWorkouts()
        {
            // --- ARRANGE ---
            var db = GetInMemoryDbContext();
            db.PlannedWorkouts.Add(new PlannedWorkout { ClientId = "client-1", Title = "Active 1", Status = "Zaplanowany", StartDate = DateTime.UtcNow.AddDays(1) });
            db.PlannedWorkouts.Add(new PlannedWorkout { ClientId = "client-1", Title = "Cancelled", Status = "Odwołany", StartDate = DateTime.UtcNow.AddDays(2) });
            db.PlannedWorkouts.Add(new PlannedWorkout { ClientId = "client-1", Title = "Active 2", Status = "Zaplanowany", StartDate = DateTime.UtcNow.AddDays(3) });
            db.PlannedWorkouts.Add(new PlannedWorkout { ClientId = "client-2", Title = "Other Client", Status = "Zaplanowany", StartDate = DateTime.UtcNow.AddDays(1) });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var service = new WorkoutService(db, new Mock<INotificationService>().Object);

            // --- ACT ---
            var result = await service.GetClientWorkoutsAsync("client-1");

            // --- ASSERT ---
            Assert.Equal(2, result.Count);
            Assert.All(result, w => Assert.NotEqual("Odwołany", w.Status));
            Assert.All(result, w => Assert.Equal("client-1", w.ClientId));
        }
    }
}
