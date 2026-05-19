using AplikacjaNaSiłownię.Models;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;

namespace AplikacjaNaSiłownię.Services
{
    public interface IUserService
    {
        Task<bool> DeleteUserAsync(string userId, string deletedByRole);
        Task<bool> ApproveTrainerAsync(string userId);
        Task<(bool HasDependencies, string Message)> CheckUserDependenciesAsync(string userId);

        /// <summary>
        /// Bezpiecznie aktualizuje zdjęcie profilowe użytkownika.
        /// </summary>
        /// <param name="userId"> ID użytkownika</param>
        /// <param name="file">Przesłany plik</param>
        /// <param name="webRootPath">Ścieżka do folderu wwwroot (do zapisu pliku)</param>
        Task<bool> UpdateProfilePictureAsync(string userId, IFormFile file, string webRootPath);
        Task<bool> RejectTrainerAsync(string userId);
        Task<IdentityResult> ChangePasswordAsync(string userId, string currentPassword, string newPassword);
        Task<bool> TerminateCollaborationAsync(string clientId, string reason, string endedByRole, string performingUserId);
    }
}
