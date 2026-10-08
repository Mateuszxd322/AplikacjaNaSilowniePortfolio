using AplikacjaNaSiłownię.Data;
using AplikacjaNaSiłownię.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AplikacjaNaSiłownię.Services
{
    // BackgroundService (usługa działająca w tle). 
    // Działa niezależnie od tego, czy ktokolwiek klika w przeglądarce. 
    // Działa jako "niewidzialny robot", który co określony czas budzi się i wykonuje zadanie.
    // W naszym przypadku: co 30 minut sprawdza, czy ktoś ma zaplanowany trening jutro i wysyła przypomnienie.
    public class WorkoutReminderService(
        IServiceProvider serviceProvider,
        ILogger<WorkoutReminderService> logger) : BackgroundService
    {
        // IServiceProvider pozwala nam tworzyć tymczasowe "zakresy" (Scopes), żeby bezpiecznie pobierać serwisy
        private readonly IServiceProvider _serviceProvider = serviceProvider;
        
        // Logger służy do zapisywania informacji o działaniu usługi w konsoli serwera
        private readonly ILogger<WorkoutReminderService> _logger = logger;

        // Główna pętla naszej usługi w tle. Uruchamia się raz przy starcie aplikacji i działa przez cały czas życia serwera.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Usługa automatycznych przypomnień o treningach została uruchomiona.");

            // Pętla działa dopóki serwer nie zostanie wyłączony (co sygnalizuje stoppingToken)
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Budzimy robota i wywołujemy naszą metodę sprawdzającą treningi
                    await CheckAndSendRemindersAsync();
                }
                catch (Exception ex)
                {
                    // W razie błędu zapisujemy go w logach serwera, ale nie wyłączamy całej usługi!
                    _logger.LogError(ex, "Wystąpił błąd podczas sprawdzania przypomnień o treningach.");
                }

                // Usypiamy robota na 30 minut. Dzięki temu oszczędzamy zasoby procesora i bazy danych.
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }

        // Metoda sprawdzająca, które treningi odbywają się za dokładnie 24 godziny (z dokładnością do 30 min) i wysyłająca powiadomienia.
        private async Task CheckAndSendRemindersAsync()
        {
            // Ponieważ ta usługa działa w tle jako "Singleton" (jedna instancja na całą aplikację), 
            // nie możemy bezpośrednio wstrzyknąć do niej bazy danych (która jest typu Scoped).
            // Rozwiązujemy to za pomocą tzw. "Scope" – tworzymy tymczasowy zakres, pobieramy bazę, a po zakończeniu automatycznie ją zwalniamy.
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            // Pobieramy aktualny czas w Polsce
            var now = TimeHelper.GetPolishTime();
            
            // Definiujemy przedział czasowy jutrzejszego dnia (za około 24 godziny)
            var targetTimeStart = now.AddHours(23).AddMinutes(30);
            var targetTimeEnd = now.AddHours(24).AddMinutes(30);

            // Wyciągamy z bazy tylko te treningi, które są "Zaplanowane", przypomnienie jeszcze nie zostało wysłane,
            // i odbywają się jutro w określonym przedziale czasowym. Zapobiega to zbędnym zapytaniom.
            var upcomingWorkouts = await db.PlannedWorkouts
                .Where(w => w.Status == "Zaplanowany" && !w.IsReminderSent && w.StartDate >= targetTimeStart && w.StartDate <= targetTimeEnd)
                .ToListAsync();

            // Przechodzimy w pętli przez wszystkie znalezione treningi
            foreach (var workout in upcomingWorkouts)
            {
                // Wysyłamy powiadomienie do klienta
                await notificationService.SendNotificationAsync(
                    workout.ClientId,
                    "Trening już jutro!",
                    $"Przypominamy o Twoim zaplanowanym treningu '{workout.Title}' jutro o godzinie {workout.StartDate:HH:mm}. Zapraszamy!",
                    "Trening"
                );

                // Oznaczamy w bazie, że przypomnienie zostało wysłane, co zapobiega dublowaniu
                workout.IsReminderSent = true;

                // Zapisujemy fakt wysłania w konsoli serwera
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Wysłano przypomnienie o treningu ID: {WorkoutId} dla klienta {ClientId}", workout.Id, workout.ClientId);
                }
            }

            // Zapisujemy zaktualizowane flagi przypomnień w bazie danych za jednym razem
            if (upcomingWorkouts.Count > 0)
            {
                await db.SaveChangesAsync();
            }
        }
    }
}
