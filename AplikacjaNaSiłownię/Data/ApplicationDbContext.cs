using AplikacjaNaSiłownię.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AplikacjaNaSiłownię.Data
{
    // Wykorzystujemy Primary Constructor (C# 12) dla DbContext, co pozwala na przekazanie opcji bezpośrednio do klasy bazowej.
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
    {


        // Używamy '= default!;' aby poinformować kompilator, że te właściwości zostaną zainicjalizowane przez Entity Framework, 
        // co eliminuje ostrzeżenie o braku wartości w konstruktorze.
        public DbSet<PlannedWorkout> PlannedWorkouts { get; set; } = default!;
        public DbSet<CollaborationHistory> CollaborationHistories { get; set; } = default!;
        public DbSet<Notification> Notifications { get; set; } = default!;
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<ApplicationUser>()
                .HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(u => u.TrainerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Indeks optymalizujący wyszukiwanie klientów dla konkretnego trenera
            builder.Entity<ApplicationUser>()
                .HasIndex(u => u.TrainerId);

            // Usunięto redundantny indeks na ClientId (pokryty przez indeks złożony)
            // Dodano indeks na PendingTrainerId w celu przyspieszenia wyszukiwania oczekujących zaproszeń
            builder.Entity<ApplicationUser>()
                .HasIndex(u => u.PendingTrainerId);

            // Indeks złożony optymalizujący pobieranie i sortowanie treningów klienta w kalendarzu i statystykach
            builder.Entity<PlannedWorkout>()
                .HasIndex(w => new { w.ClientId, w.StartDate });

            // Indeksy optymalizujące pobieranie historii współpracy dla klienta i trenera
            builder.Entity<CollaborationHistory>()
                .HasIndex(h => h.ClientId);

            builder.Entity<CollaborationHistory>()
                .HasIndex(h => h.TrainerId);

            // Twarde klucze obce dla historii współpracy z regułą Restrict w celu uniknięcia cykli w SQL Server
            builder.Entity<CollaborationHistory>()
                .HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(h => h.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<CollaborationHistory>()
                .HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(h => h.TrainerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Złożony indeks optymalizujący pobieranie i sortowanie nieprzeczytanych powiadomień 
            // dla dzwoneczka powiadomień użytkownika (najnowsze u góry).
            builder.Entity<Notification>()
                .HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt });

            // Twardy klucz obcy dla powiadomień z automatycznym czyszczeniem kaskadowym (CASCADE)
            builder.Entity<Notification>()
                .HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
