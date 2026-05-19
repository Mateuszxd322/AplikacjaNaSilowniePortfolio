using System.Runtime.CompilerServices;

namespace AplikacjaNaSiłownię.Models.ViewModels
{
    // Obiekt transferu danych (DTO) do przesyłania żądania zmiany hasła przez AJAX.
    public class ChangePasswordDto
    {
        // Obecne hasło użytkownika (wymagane do weryfikacji tożsamości).
        public string CurrentPassword { get; set; } = string.Empty;

        // Nowe hasło spełniające wymogi polityki bezpieczeństwa.
        public string NewPassword { get; set; } = string.Empty;

        // Powtórzenie nowego hasła w celu uniknięcia literówek.
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}