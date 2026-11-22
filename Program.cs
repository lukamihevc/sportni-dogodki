using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration
    .GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Manjka povezovalni niz DefaultConnection."
    );

builder.Services.AddSingleton(new Baza(connectionString));

// Avtentikacija in avtorizacija
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "SportniDogodki.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Events.OnRedirectToLogin = context =>
        {
            // Za API klice vrnemo 401 namesto HTML preusmeritve na /Account/Login
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            // Prijavljen uporabnik brez ustrezne vloge dobi 403 namesto preusmeritve
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

await app.Services.GetRequiredService<Baza>().InicializirajAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.StatusCode =
                StatusCodes.Status500InternalServerError;

            await context.Response.WriteAsJsonAsync(
                new { error = "Prislo je do napake na strezniku." }
            );
        });
    });

    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet(
    "/",
    () => Results.Text(
        """
        Sportni dogodki API

        Razpolozljive povezave:

        /health                           - Preverjanje delovanja aplikacije
        POST /api/uporabniki/registracija - Registracija uporabnika
        POST /api/uporabniki/prijava      - Prijava uporabnika
        POST /api/uporabniki/odjava       - Odjava uporabnika
        GET  /api/uporabniki/trenutni     - Podatki prijavljenega uporabnika (zaščiteno)
        POST /api/tekmovanja/{id}/prijave - Prijava ekipe na tekmovanje (vodja ekipe)
        DELETE /api/tekmovanja/{id}/prijave/{prijavaId} - Preklic prijave (vodja ekipe)
        GET  /api/obvestila               - Obvestila prijavljenega uporabnika (zaščiteno)
        """,
        "text/plain; charset=utf-8"
    )
);

app.MapGet("/health", () => Results.Ok("OK"));

app.MapRegistracijaUporabnika();
app.MapAvtentikacijaUporabnika();
app.MapPrijavaEkipe();
app.MapPreklicPrijave();
app.MapObvestila();

app.Run();

public partial class Program { }