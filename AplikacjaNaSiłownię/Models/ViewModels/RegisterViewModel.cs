using System.ComponentModel.DataAnnotations;

namespace AplikacjaNaSiłownię.Models.ViewModels
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Imię jest wymagane")]
        [StringLength(100, ErrorMessage = "Imię nie może być dłuższe niż 100 znaków.")]
        public string Imie { get; set; } = string.Empty;

        [Required(ErrorMessage = "Nazwisko jest wymagane")]
        [StringLength(100, ErrorMessage = "Nazwisko nie może być dłuższe niż 100 znaków.")]
        public string Nazwisko { get; set; } = string.Empty;

        [Required(ErrorMessage = "Login/Email jest wymagany")]
        [Display(Name = "Login / Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Hasło jest wymagane")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Potwierdź hasło")]
        [Compare("Password", ErrorMessage = "Hasła nie pasują do siebie.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Wybór roli jest wymagany")]
        public string Rola { get; set; } = "Klient"; 
    }
}
