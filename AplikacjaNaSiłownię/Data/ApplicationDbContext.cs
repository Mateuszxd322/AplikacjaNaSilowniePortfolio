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
                .HasForeignKey(u => u.TrenerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Indeks optymalizujący wyszukiwanie klientów dla konkretnego trenera
            builder.Entity<ApplicationUser>()
                .HasIndex(u => u.TrenerId);

            // Indeks optymalizujący pobieranie treningów dla konkretnego klienta
            builder.Entity<PlannedWorkout>()
                .HasIndex(w => w.ClientId);

            // Indeks złożony optymalizujący pobieranie i sortowanie treningów klienta w kalendarzu i statystykach
            builder.Entity<PlannedWorkout>()
                .HasIndex(w => new { w.ClientId, w.StartDate });

            // Indeksy optymalizujące pobieranie historii współpracy dla klienta i trenera
            builder.Entity<CollaborationHistory>()
                .HasIndex(h => h.ClientId);

            builder.Entity<CollaborationHistory>()
                .HasIndex(h => h.TrenerId);

            builder.Entity<Notification>()
                    .HasIndex(n => n.UserId);
        }
    }
}
