using System;
using System.ComponentModel.DataAnnotations;

namespace AplikacjaNaSiłownię.Models
{
    // Model reprezentujący powiadomienie użytkownika w bazie danych.
    public class Notification
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(450)]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Title { get; set; } = string.Empty;

        [Required]
        // Limit podniesiony do 1000, aby zapobiec wyciekom/błędom obcięcia danych (truncation error) przy długich powodach rezygnacji
        [MaxLength(1000)]
        public string Message { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public bool IsRead { get; set; } = false;

        [MaxLength(50)]
        public string Type { get; set; } = string.Empty; // np. "Zaproszenie", "Trening", "KoniecWspolpracy"
    }
}
