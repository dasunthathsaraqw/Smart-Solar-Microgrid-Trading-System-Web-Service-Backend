/**
 * File: Program.cs
 * Purpose: Application entry point — configures services, JWT auth, CORS, Swagger and middleware, then seeds the database.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using SmartMicrogrid.API.Config;
using SmartMicrogrid.API.Data;
using SmartMicrogrid.API.Services;

var builder = WebApplication.CreateBuilder(args);

// IIS app-pool identities may not write Windows EventLog; Console and Debug are safe defaults.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Bind strongly-typed configuration sections.
builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDB"));
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));

// Register application services.
builder.Services.AddSingleton<IMongoDbService, MongoDbService>();
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IProsumerService, ProsumerService>();
builder.Services.AddScoped<IReservationService, ReservationService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IStationService, StationService>();
builder.Services.AddScoped<ISlotService, SlotService>();

// Controllers with camelCase JSON output to match the JavaScript frontend.
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// CORS applies to browser frontends only; native Android requests do not require their phone IP here.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader();
    });
});

// JWT bearer authentication configured from the bound Jwt settings.
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
if (jwtSettings.Key == "REPLACE_WITH_YOUR_OWN_SECRET_AT_LEAST_32_CHARS_LONG" || jwtSettings.Key.Length < 32)
{
    throw new InvalidOperationException("Jwt:Key must be a private value of at least 32 characters. Set it with dotnet user-secrets set \"Jwt:Key\" <key> or the Jwt__Key environment variable.");
}
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
    };
});

builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlDocumentation = $"{typeof(Program).Assembly.GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlDocumentation));
});

var app = builder.Build();

// Log unexpected failures server-side while returning a stable, non-sensitive JSON response.
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(exception, "Unhandled request failure for trace {TraceId}", context.TraceIdentifier);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred", traceId = context.TraceIdentifier });
    });
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// LAN Android clients can use plain HTTP when TLS has not been provisioned on IIS.
if (builder.Configuration.GetValue<bool>("Hosting:UseHttpsRedirection"))
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// A lightweight landing page confirms that the ASP.NET Core app responded; database health is checked separately.
app.MapGet("/", () => Results.Content("""
    <!doctype html>
    <html lang="en">
    <head>
      <meta charset="utf-8">
      <meta name="viewport" content="width=device-width, initial-scale=1">
      <title>Smart Solar Microgrid API</title>
      <style>
        :root { color-scheme: light; font-family: system-ui, sans-serif; }
        body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: #f3f7f5; color: #18332b; }
        main { box-sizing: border-box; width: min(92%, 620px); padding: 2.5rem; background: #fff; border: 1px solid #dce9e1; border-radius: 18px; box-shadow: 0 12px 36px #18332b12; }
        .brand { margin: 0 0 1.5rem; color: #287a50; font-weight: 700; letter-spacing: .03em; }
        h1 { margin: 0 0 1rem; font-size: clamp(2rem, 5vw, 2.75rem); }
        .badge { display: inline-block; padding: .35rem .8rem; border-radius: 999px; background: #e2f5e8; color: #17643d; font-weight: 700; }
        p { line-height: 1.6; }
        a { color: #17643d; font-weight: 600; }
      </style>
    </head>
    <body>
      <main>
        <p class="brand">Smart Solar Microgrid</p>
        <h1>API is running.</h1>
        <span class="badge">Online</span>
        <p>This server hosts the solar station, user, and reservation API. API routes are under <strong>/api</strong>.</p>
        <p>Check database health at <a href="api/health">GET /api/health</a>.</p>
      </main>
    </body>
    </html>
    """, "text/html; charset=utf-8"));

// Seed the initial Backoffice admin user if the Users collection is empty.
// Wrapped in try/catch so a MongoDB outage at startup logs a warning instead of
// crashing the whole process before Kestrel ever starts listening.
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<IMongoDbService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await DbSeeder.SeedAsync(db, passwordHasher);
        await MongoIndexSeeder.CreateAsync(db, app.Logger);
        if (builder.Configuration.GetValue<bool>("Seeding:SeedSampleData"))
        {
            await SampleDataSeeder.SeedAsync(db, passwordHasher);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not seed the database on startup. Is MongoDB reachable?");
    }
}

app.Run();

// Exposes the minimal-hosting entry point to WebApplicationFactory integration tests.
public partial class Program { }
