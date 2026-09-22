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
