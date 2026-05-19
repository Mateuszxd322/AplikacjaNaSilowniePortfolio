using System;
using System.ComponentModel.DataAnnotations;

namespace AplikacjaNaSiłownię.Models
{
    public class CollaborationHistory
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(450)]
        public string ClientId { get; set; } = string.Empty;

        [MaxLength(200)]
        public string ClientName { get; set; } = string.Empty;

        [MaxLength(450)]
        public string TrenerId { get; set; } = string.Empty;
        [MaxLength(200)]
        public string TrenerName { get; set; } = string.Empty;

        public DateTime EndDate { get; set; } = AplikacjaNaSiłownię.Helpers.TimeHelper.GetPolishTime();

        // Kto zakończył: "Klient", "Trener" lub "Administrator"
        [MaxLength(50)]
        public string EndedByRole { get; set; } = string.Empty;

        [MaxLength(500, ErrorMessage = "Powód nie może być dłuższy niż 500 znaków.")]
        public string Reason { get; set; } = string.Empty;

        // Flagi do zarządzania usuwaniem (Soft-Delete)
        public bool IsDeletedByClient { get; set; } = false;
        public bool IsDeletedByTrainer { get; set; } = false;
    }
}
