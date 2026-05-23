using AplikacjaNaSiłownię.Data;
using AplikacjaNaSiłownię.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("AplikacjaNaSilownie_DB") ?? throw new InvalidOperationException("Connection string not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// TODO: W produkcji użyć silniejszych polityk haseł
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(1);
    options.Lockout.MaxFailedAccessAttempts = 5;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddScoped<AplikacjaNaSiłownię.Services.INotificationService, AplikacjaNaSiłownię.Services.NotificationService>();
builder.Services.AddHostedService<AplikacjaNaSiłownię.Services.WorkoutReminderService>();

// Rejestracja serwisów (Service Layer)
builder.Services.AddScoped<AplikacjaNaSiłownię.Services.IUserService, AplikacjaNaSiłownię.Services.UserService>();
builder.Services.AddScoped<AplikacjaNaSiłownię.Services.IWorkoutService, AplikacjaNaSiłownię.Services.WorkoutService>();

// Konfiguracja Antiforgery dla zapytań fetch (CSRF)
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

// Konfiguracja Rate Limitingu z partycjonowaniem po adresie IP klienta.
// Zapobiega to atakom Denial of Service (DoS) na poziomie globalnym – ograniczenie liczby żądań
// dotyczy pojedynczego IP, więc próba brute-force lub przeciążenia serwera z jednego źródła nie zablokuje dostępu innym użytkownikom.
builder.Services.AddRateLimiter(options =>
    {
        options.AddPolicy("LoginPolicy", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "global",
                factory: partition => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,                      // Użytkownik z danego IP może wykonać maksymalnie 5 prób...
                    Window = TimeSpan.FromSeconds(10),     // ...w ciągu każdych 10 sekund.
                    QueueLimit = 0,                        // Brak kolejkowania - nadmiarowe żądania są natychmiast odrzucane.
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                }));

        // Ustawiamy kod odpowiedzi HTTP 429 (Too Many Requests) dla odrzuconych żądań.
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // Customowy handler wywoływany w momencie przekroczenia limitu żądań (np. 5 prób w ciągu 10 sekund).
        // Zapobiega wyświetlaniu domyślnego, mało przyjaznego błędu przeglądarki przy statusie 429.
        options.OnRejected = async (context, token) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

            // Sprawdzamy nagłówek Accept, aby ustalić czy żądanie oczekuje widoku HTML (przeglądarka) czy JSON (AJAX)
            var acceptsHtml = context.HttpContext.Request.Headers.Accept.ToString().Contains("text/html");

            if (acceptsHtml)
            {
                // Zwracamy spersonalizowany, responsywny widok HTML wczytany z folderu wwwroot
                context.HttpContext.Response.ContentType = "text/html; charset=utf-8";
                var env = context.HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
                var htmlPath = Path.Combine(env.WebRootPath, "errors", "Error_429.html");

                if (File.Exists(htmlPath))
                {
                    // Wczytujemy asynchronicznie zawartość pliku Error_429.html, aby nie blokować wątku serwera
                    var html = await File.ReadAllTextAsync(htmlPath, token);
                    await context.HttpContext.Response.WriteAsync(html, token);
                }
                else
                {
                    // Zabezpieczenie awaryjne (fallback) wyświetlające podstawowy komunikat w razie braku pliku na serwerze
                    await context.HttpContext.Response.WriteAsync("<h1>Zbyt wiele żądań</h1><p>Ze względów bezpieczeństwa ograniczono liczbę połączeń z Twojego IP. Spróbuj ponownie później.</p>", token);
                }
            }
            else
            {
                // Dla zapytań asynchronicznych (np. fetch z panelu logowania) zwracamy ustrukturyzowany JSON
                context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
                await context.HttpContext.Response.WriteAsJsonAsync(new { success = false, message = "Zbyt wiele prób logowania. Możliwość operacji została tymczasowo ograniczona. Spróbuj ponownie za chwilę." }, token);
            }
        };
    }
);

var app = builder.Build();

// Skrypt zasiewający konto Administratora: Admin, admin
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    if (!roleManager.Roles.Any())
    {
        roleManager.CreateAsync(new IdentityRole("Admin")).Wait();
        roleManager.CreateAsync(new IdentityRole("Trener")).Wait();
        roleManager.CreateAsync(new IdentityRole("Klient")).Wait();
    }

    if (userManager.FindByNameAsync("Admin").Result == null)
    {
        var adminUser = new ApplicationUser
        {
            UserName = "Admin",
            Email = "admin@system.local",
            Imie = "Główny",
            Nazwisko = "Administrator",
            Rola = "Admin"
        };
        // TODO: W produkcji użyć bezpieczniejszych metod inicjalizacji hasła (np. Secrets/Environment Variables)
        if (userManager.CreateAsync(adminUser, "admin").Result.Succeeded)
        {
            userManager.AddToRoleAsync(adminUser, "Admin").Wait();
        }
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}


/*
 * --- KONFIGURACJA POTOKU ŻĄDAŃ HTTP (MIDDLEWARE PIPELINE) ---
 * 1. HttpsRedirection: Wymusza szyfrowane połączenie HTTPS.
 * 2. StaticFiles: Udostępnia pliki statyczne z folderu 'wwwroot' (CSS, JS, obrazki).
 * 3. Routing: Dopasowuje adres URL do odpowiedniej ścieżki (kontrolera) w aplikacji.
 * 4. RateLimiter: Ochrona przed spamem/atakiem DDoS poprzez limit zapytań z jednego IP.
 * 5. Authentication: Rozpoznaje kim jest zalogowany użytkownik (MUSI być przed autoryzacją).
 * 6. Authorization: Sprawdza, czy zalogowany użytkownik ma uprawnienia (np. odpowiednią rolę).
 * 7. MapStaticAssets: Nowoczesna optymalizacja wydajności serwowania plików w .NET.
 * 8. MapControllerRoute: Ustawia domyślny wzorzec adresów URL (np. /Home/Index).
 * 9. MapHub: Konfiguruje i otwiera połączenia w czasie rzeczywistym (SignalR / WebSockets).
 * 10. Run: Ostatecznie uruchamia główny serwer nasłuchujący zapytań.
 */
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<AplikacjaNaSiłownię.Hubs.NotificationHub>("/notificationHub");
app.Run();
