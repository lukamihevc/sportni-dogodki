var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

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

        /health - Preverjanje delovanja aplikacije
        """,
        "text/plain; charset=utf-8"
    )
);

app.MapGet("/health", () => Results.Ok("OK"));


app.Run();