using AplikacjaNaSiłownię.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Threading.Tasks;

namespace AplikacjaNaSiłownię.Helpers
{
    // Customowa fabryka tożsamości (ClaimsPrincipal), która integruje kolumnę "Rola" z systemem autoryzacji.
    // Dzięki temu rezygnujemy z tabeli łączącej AspNetUserRoles (brak dublowania ról).
    public class CustomUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        IOptions<IdentityOptions> optionsAccessor)
        : UserClaimsPrincipalFactory<ApplicationUser>(userManager, optionsAccessor)
    {
        // Metoda wywoływana podczas logowania. Generuje claimsy (oświadczenia) użytkownika.
        protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
        {
            // Generujemy standardowe claimsy (ID, email itp.)
            var identity = await base.GenerateClaimsAsync(user);

            // Pobieramy rolę z kolumny i wstrzykujemy jako standardowy Claim roli.
            // Dzięki temu [Authorize(Roles = "...")] i @User.IsInRole(...) działają automatycznie!
            if (!string.IsNullOrEmpty(user.Role))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, user.Role));
            }

            return identity;
        }
    }
}
