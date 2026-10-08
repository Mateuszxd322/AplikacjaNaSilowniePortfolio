using System.ComponentModel.DataAnnotations;

namespace AplikacjaNaSiłownię.Models.ViewModels
{
    // Obiekt transferu danych (DTO) dla żądań zapisu i edycji treningu wysyłanych z kalendarza (AJAX).
    // Kontrolery przyjmują DTO zamiast encji PlannedWorkout, dzięki czemu klient nie może nadpisać pól,
    // którymi zarządza wyłącznie serwer (Status, IsReminderSent, Client) - ochrona przed Over-Postingiem.
    public class WorkoutDto
    {
        public int Id { get; set; }

        // Wymagane tylko wtedy, gdy trener planuje trening klientowi. Klient zawsze planuje samemu sobie.
        public string? ClientId { get; set; }

        [Required]
        public DateTime StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        [Required(ErrorMessage = "Tytuł treningu jest wymagany.")]
        [MaxLength(100, ErrorMessage = "Tytuł nie może być dłuższy niż 100 znaków.")]
        public string Title { get; set; } = string.Empty;

        [MaxLength(500, ErrorMessage = "Opis nie może być dłuższy niż 500 znaków.")]
        public string? Description { get; set; }

        // Mapowanie na encję. Id przenosimy tylko przy edycji - przy dodawaniu nadaje je baza danych.
        public PlannedWorkout ToEntity(bool includeId) => new()
        {
            Id = includeId ? Id : 0,
            ClientId = ClientId ?? string.Empty,
            StartDate = StartDate,
            EndDate = EndDate,
            Title = Title,
            Description = Description
        };
    }
}
