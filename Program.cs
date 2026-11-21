var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration
    .GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Manjka povezovalni niz DefaultConnection."
    );

builder.Services.AddSingleton(new Baza(connectionString));

var app = builder.Build();

// Ustvari bazo in tabele ob zagonu
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

app.MapGet(
    "/",
    () => Results.Text(
        """
        Sportni dogodki API

        Razpolozljive povezave:

        /health                           - Preverjanje delovanja aplikacije
        POST /api/uporabniki/registracija - Registracija uporabnika
        """,
        "text/plain; charset=utf-8"
    )
);

app.MapGet("/health", () => Results.Ok("OK"));

app.MapRegistracijaUporabnika();

app.Run();