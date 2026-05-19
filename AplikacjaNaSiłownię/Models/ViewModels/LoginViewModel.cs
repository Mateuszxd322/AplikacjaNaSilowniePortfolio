using System.ComponentModel.DataAnnotations;

namespace AplikacjaNaSiłownię.Models.ViewModels
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Login jest wymagany")]
        [Display(Name = "Login / Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Hasło jest wymagane")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

    }
}
