using System.ComponentModel.DataAnnotations;
using AplikacjaNaSiłownię.Helpers;

namespace AplikacjaNaSiłownię.Models
{
    public class PlannedWorkout
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string ClientId { get; set; } = string.Empty;
        public ApplicationUser? Client { get; set; }

        [Required]
        public DateTime StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        [Required(ErrorMessage = "Tytuł treningu jest wymagany.")]
        [MaxLength(100, ErrorMessage = "Tytuł nie może być dłuższy niż 100 znaków.")]
        public string Title { get; set; } = string.Empty;

        // Opis jest opcjonalny, ale ograniczony długością dla stabilności bazy danych.
        [MaxLength(500, ErrorMessage = "Opis nie może być dłuższy niż 500 znaków.")]
        public string? Description { get; set; }

        [MaxLength(50)]
        public string? Status { get; set; } = "Zaplanowany";

        /// <summary>
        /// Logika obliczająca stan treningu na podstawie czasu. 
        /// Centralizacja tego w modelu zapobiega błędom i ułatwia wprowadzanie zmian w całej aplikacji.
        /// </summary>
        public string GetFinalStatus()
        {
            if (Status == "Odwołany") return "Odwołany";
            
            var endTime = EndDate ?? StartDate;
            if (endTime < TimeHelper.GetPolishTime()) return "Zrealizowany";
            
            return Status ?? "Zaplanowany";
        }

        /// <summary>
        /// Zwraca odpowiednią klasę CSS dla FullCalendar w zależności od stanu treningu.
        /// </summary>
        public string GetCssClass()
        {
            return GetFinalStatus() == "Zrealizowany" ? "gym-event-completed" : "gym-event-planned";
        }
    }
}
