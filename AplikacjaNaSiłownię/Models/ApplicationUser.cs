using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace AplikacjaNaSiłownię.Models
{
    public class ApplicationUser : IdentityUser
    {
        // Atrybuty MaxLength chronią bazę danych przed atakami typu "Long String Injection"
        // oraz niepotrzebnym zużyciem pamięci RAM przy przetwarzaniu ogromnych ilości tekstu.
        [MaxLength(100)]
        public string Imie { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Nazwisko { get; set; } = string.Empty;

        // Biała lista ról jest dodatkowo weryfikowana w kontrolerze (AccountController).
        [MaxLength(50)]
        public string Rola { get; set; } = "Klient";

        // Id Trenera, przydatne tylko gdy Rola to Klient. Self-referencing do IdentityUser.Id
        [MaxLength(450)]
        public string? TrenerId { get; set; }

        // Id Trenera od którego Klient otrzymał zaproszenie (oczekujące na akceptację)
        [MaxLength(450)]
        public string? PendingTrenerId { get; set; }

        // Czy konto zostało zatwierdzone przez administratora (domyślnie tak, chyba że ktoś rejestruje się jako Trener)
        public bool IsApproved { get; set; } = true;

        [MaxLength(500)]
        public string? ProfilePicturesPath { get; set; }
    }
}
