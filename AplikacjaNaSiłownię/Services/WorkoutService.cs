using AplikacjaNaSiłownię.Data;
using AplikacjaNaSiłownię.Models;
using Microsoft.EntityFrameworkCore;
using System;
using AplikacjaNaSiłownię.Helpers;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AplikacjaNaSiłownię.Services
{
    // Serwis obsługujący operacje na zaplanowanych treningach, zapewniający walidację ról i dat.
    public class WorkoutService(
        ApplicationDbContext context,
        AplikacjaNaSiłownię.Services.INotificationService notificationService) : IWorkoutService
    {
        private readonly ApplicationDbContext _context = context;
        private readonly AplikacjaNaSiłownię.Services.INotificationService _notificationService = notificationService;

        public async Task<bool> AddWorkoutAsync(PlannedWorkout workout, string creatorId, string userRole)
        {
            // Centralna walidacja daty - nie pozwalamy na planowanie treningów "wstecz"
            var now = TimeHelper.GetPolishTime();
            var nowTrimmed = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            if (workout.StartDate < nowTrimmed) return false;

            // KROK: Walidacja logiczna czasu trwania (Naprawa Punktu 1 Audytu 2.0)
            if (workout.EndDate.HasValue && workout.EndDate.Value <= workout.StartDate) return false;

            // Logika specyficzna dla ról:
            // Trener może dodawać treningi tylko swoim przypisanym klientom.
            if (userRole == "Trener")
            {
                // AsNoTracking() poprawia wydajność, bo tylko sprawdzamy istnienie rekordu bez zamiaru jego edycji.
                var client = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == workout.ClientId);
                if (client == null || client.TrenerId != creatorId) return false;
            }
            else if (userRole == "Klient")
            {
                // Klient zawsze dodaje trening samemu sobie.
                workout.ClientId = creatorId;
            }

            if (workout.EndDate == null) workout.EndDate = workout.StartDate.AddHours(1);
            workout.Status = "Zaplanowany";

            // WAŻNE: Czyścimy obiekt Client przed zapisem. 
            // Zapobiega to konfliktom w Entity Framework i błędom serializacji JSON przy odpowiedzi serwera.
            workout.Client = null;

            _context.PlannedWorkouts.Add(workout);
            await _context.SaveChangesAsync();


            if (userRole == "Trener")
            {
                await _notificationService.SendNotificationAsync(
                    workout.ClientId,
                    "Nowy trening zaplanowany!",
                    $"Twój trener zaplanował nowy trening '{workout.Title}' na dzień {workout.StartDate:dd.MM.yyyy HH:mm}.",
                    "Trening"
                );
            }
            else if (userRole == "Klient")
            {
                var clientUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == workout.ClientId);
                if (clientUser != null && !string.IsNullOrEmpty(clientUser.TrenerId))
                {
                    await _notificationService.SendNotificationAsync(
                        clientUser.TrenerId,
                        "Nowy trening zaplanowany!",
                        $"Twój klient {clientUser.Imie} {clientUser.Nazwisko} zaplanował nowy trening '{workout.Title}' na dzień {workout.StartDate:dd.MM.yyyy HH:mm}.",
                        "Trening"
                    );
                }
            }

            return true;
        }

        public async Task<bool> UpdateWorkoutAsync(PlannedWorkout model, string editorId, string userRole)
        {
            var workout = await _context.PlannedWorkouts
                                        .Include(w => w.Client)
                                        .FirstOrDefaultAsync(w => w.Id == model.Id);

            if (workout == null) return false;

            // Weryfikacja uprawnień: Admin może wszystko, Trener swoich klientów, Klient swoje własne
            if (userRole == "Trener")
            {
                if (workout.Client == null || workout.Client.TrenerId != editorId) return false;
            }
            else if (userRole == "Klient")
            {
                if (workout.ClientId != editorId) return false;
            }

            // Walidację daty rozpoczęcia wykonujemy tylko przy jej rzeczywistej zmianie.
            // Zapobiega to blokowaniu edycji innych pól (np. opisu czy tytułu) dla treningów, które już się odbyły lub trwają.
            if (workout.StartDate != model.StartDate)
            {
                var now = TimeHelper.GetPolishTime();
                var nowTrimmed = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
                if (model.StartDate < nowTrimmed) return false;
            }

            // KROK: Walidacja logiczna czasu trwania przy edycji
            if (model.EndDate.HasValue && model.EndDate.Value <= model.StartDate) return false;

            workout.Title = model.Title;
            workout.Description = model.Description;
            workout.StartDate = model.StartDate;
            workout.EndDate = model.EndDate;

            await _context.SaveChangesAsync();


            if (userRole == "Trener")
            {
                await _notificationService.SendNotificationAsync(
                    workout.ClientId,
                    "Trening zaktualizowany!",
                    $"Twój trener zmienił szczegóły treningu '{workout.Title}'. Nowy termin: {workout.StartDate:dd.MM.yyyy HH:mm}.",
                    "Trening"
                );
            }
            else if (userRole == "Klient")
            {
                if (workout.Client != null && !string.IsNullOrEmpty(workout.Client.TrenerId))
                {
                    await _notificationService.SendNotificationAsync(
                        workout.Client.TrenerId,
                        "Trening zaktualizowany!",
                        $"Klient {workout.Client.Imie} {workout.Client.Nazwisko} zmienił szczegóły treningu '{workout.Title}'. Nowy termin: {workout.StartDate:dd.MM.yyyy HH:mm}.",
                        "Trening"
                    );
                }
            }
            else if (userRole == "Admin")
            {
                // Powiadom klienta
                await _notificationService.SendNotificationAsync(
                    workout.ClientId,
                    "Trening zaktualizowany przez Admina!",
                    $"Administrator systemu zmienił szczegóły Twojego treningu '{workout.Title}'. Nowy termin: {workout.StartDate:dd.MM.yyyy HH:mm}.",
                    "Trening"
                );

                // Powiadom trenera (jeśli klient go ma)
                if (workout.Client != null && !string.IsNullOrEmpty(workout.Client.TrenerId))
                {
                    await _notificationService.SendNotificationAsync(
                        workout.Client.TrenerId,
                        "Trening zaktualizowany przez Admina!",
                        $"Administrator systemu zmienił szczegóły treningu '{workout.Title}' należącego do Twojego klienta ({workout.Client.Imie} {workout.Client.Nazwisko}). Nowy termin: {workout.StartDate:dd.MM.yyyy HH:mm}.",
                        "Trening"
                    );
                }
            }

            return true;
        }

        public async Task<bool> DeleteWorkoutAsync(int workoutId, string editorId, string userRole)
        {
            var workout = await _context.PlannedWorkouts
                                        .Include(w => w.Client)
                                        .FirstOrDefaultAsync(w => w.Id == workoutId);

            if (workout == null) return false;

            // Weryfikacja uprawnień
            if (userRole == "Trener")
            {
                if (workout.Client == null || workout.Client.TrenerId != editorId) return false;
            }
            else if (userRole == "Klient")
            {
                if (workout.ClientId != editorId) return false;
            }

            workout.Status = "Odwołany";
            await _context.SaveChangesAsync();


            if (userRole == "Trener")
            {
                await _notificationService.SendNotificationAsync(
                    workout.ClientId,
                    "Trening odwołany!",
                    $"Twój trener odwołał zaplanowany trening '{workout.Title}' na dzień {workout.StartDate:dd.MM.yyyy HH:mm}.",
                    "Trening"
                );
            }
            else if (userRole == "Klient")
            {
                if (workout.Client != null && !string.IsNullOrEmpty(workout.Client.TrenerId))
                {
                    await _notificationService.SendNotificationAsync(
                        workout.Client.TrenerId,
                        "Trening odwołany!",
                        $"Klient {workout.Client.Imie} {workout.Client.Nazwisko} odwołał zaplanowany trening '{workout.Title}' na dzień {workout.StartDate:dd.MM.yyyy HH:mm}.",
                        "Trening"
                    );
                }
            }
            else if (userRole == "Admin")
            {
                // Powiadom klienta
                await _notificationService.SendNotificationAsync(
                    workout.ClientId,
                    "Trening odwołany przez Admina!",
                    $"Administrator systemu odwołał Twój zaplanowany trening '{workout.Title}' na dzień {workout.StartDate:dd.MM.yyyy HH:mm}.",
                    "Trening"
                );

                // Powiadom trenera (jeśli klient go ma)
                if (workout.Client != null && !string.IsNullOrEmpty(workout.Client.TrenerId))
                {
                    await _notificationService.SendNotificationAsync(
                        workout.Client.TrenerId,
                        "Trening odwołany przez Admina!",
                        $"Administrator systemu odwołał zaplanowany trening '{workout.Title}' Twojego klienta ({workout.Client.Imie} {workout.Client.Nazwisko}) na dzień {workout.StartDate:dd.MM.yyyy HH:mm}.",
                        "Trening"
                    );
                }
            }

            return true;
        }

        public async Task<List<PlannedWorkout>> GetClientWorkoutsAsync(string clientId)
        {
            return await _context.PlannedWorkouts
                                 .Where(w => w.ClientId == clientId && w.Status != "Odwołany")
                                 .ToListAsync();
        }
    }
}
