using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using EFCore.NamingConventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using VoiceChat.Api.Data;
using VoiceChat.Api.Models.Entities;
using VoiceChat.Api.Hubs;
using VoiceChat.Api.Infrastructure;
using VoiceChat.Api.Interfaces;
using VoiceChat.Api.Options;
using VoiceChat.Api.Services;

// Local ".env" is git-ignored — GoogleCredentials__*, SupabaseCredentials__* (see .env.example). Never commit secrets.
LocalDotEnvLoader.TryLoad();

var builder = WebApplication.CreateBuilder(args);
// Process environment (injected by Docker / hosting dashboards) overrides appsettings.json for the same keys.
builder.Configuration.AddEnvironmentVariables();
LocalDotEnvLoader.MergeIntoConfiguration(builder.Configuration);

ConfigurationPlaceholderExpander.Apply(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "VoiceChat API",
        Version = "v1",
        Description =
            "REST API for conversations and messages. Real-time chat uses SignalR at /hubs/chat. " +
            "LLM replies use a local Ollama server (free, no cloud API keys). " +
            "Install from https://ollama.com — see README."
    });
});

// Process env SupabaseCredentials__ConnectionString maps to configuration SupabaseCredentials:ConnectionString (.NET convention).
string? T(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
var conn = T(builder.Configuration["SupabaseCredentials:ConnectionString"])
    ?? T(builder.Configuration.GetConnectionString("DefaultConnection"))
    ?? T(Environment.GetEnvironmentVariable("SupabaseCredentials__ConnectionString"))
    ?? T(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"));
if (!string.IsNullOrWhiteSpace(conn) && conn.Length >= 2 &&
    ((conn[0] == '"' && conn[^1] == '"') || (conn[0] == '\'' && conn[^1] == '\'')))
    conn = conn[1..^1].Trim();

if (string.IsNullOrWhiteSpace(conn))
{
    throw new InvalidOperationException(
        "PostgreSQL connection string missing. Set environment variable SupabaseCredentials__ConnectionString or ConnectionStrings__DefaultConnection, " +
        "or add Api/VoiceChat.Api/.env with SupabaseCredentials__ConnectionString=..., or run: dotnet user-secrets set \"SupabaseCredentials:ConnectionString\" \"...\" --project VoiceChat.Api.csproj");
}

if (conn.StartsWith("{{", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "SupabaseCredentials:ConnectionString is still a literal {{...}} placeholder. Use an empty value in appsettings and set SupabaseCredentials__ConnectionString to your Npgsql string (Host=...;Username=...;Password=...;Database=...;SSL Mode=Require;Trust Server Certificate=true).");
}

PostgresConnectionStringLogging.ThrowIfNotNpgsqlConnectionString(conn);
NpgsqlSupabaseConnection.ThrowIfPoolerNeedsTenantUsername(conn);
conn = NpgsqlSupabaseConnection.PrepareConnectionString(conn);
PostgresConnectionStringLogging.ThrowIfProductionUsesLocalOnlyHost(builder.Environment, conn);

Console.WriteLine(
    $"[VoiceChat.Api] Environment={builder.Environment.EnvironmentName}; " +
    PostgresConnectionStringLogging.FormatForConsole(conn));

builder.Services.Configure<SupabaseCredentialsOptions>(builder.Configuration.GetSection(SupabaseCredentialsOptions.SectionName));

builder.Services.AddDbContext<AppDbContext>(o =>
{
    o.UseNpgsql(conn, npgsql =>
    {
        npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
        npgsql.CommandTimeout(30);
    });
    o.UseSnakeCaseNamingConvention();
});

builder.Services
    .AddOptions<OllamaOptions>()
    .Bind(builder.Configuration.GetSection(OllamaOptions.SectionName));

builder.Services
    .AddHttpClient<ILlmClient, OllamaLlmClient>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        KeepAlivePingDelay = TimeSpan.FromSeconds(60),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
        EnableMultipleHttp2Connections = true
    })
    .ConfigureHttpClient((sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
        client.BaseAddress = opts.ResolveBaseUri();
        client.Timeout = TimeSpan.FromMinutes(5);
    });

builder.Services
    .AddHttpClient("OllamaHealth")
    .ConfigureHttpClient((sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
        client.BaseAddress = opts.ResolveBaseUri();
        client.Timeout = TimeSpan.FromSeconds(30);
    });

builder.Services.AddSingleton<ChatGenerationCancellationRegistry>();
builder.Services.AddScoped<IChatOrchestrator, ChatOrchestrator>();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.Configure<OtpOptions>(builder.Configuration.GetSection(OtpOptions.SectionName));
builder.Services.Configure<WebClientOptions>(builder.Configuration.GetSection(WebClientOptions.SectionName));
builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection(GoogleAuthOptions.SectionName));
builder.Services.Configure<PasswordResetOptions>(builder.Configuration.GetSection(PasswordResetOptions.SectionName));

builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<OtpAuthService>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddSingleton<IMailSender, SmtpMailSender>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

var jwtOpts = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
              ?? throw new InvalidOperationException("Jwt configuration is missing.");
if (string.IsNullOrWhiteSpace(jwtOpts.SigningKey) || jwtOpts.SigningKey.Length < 32)
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters.");

var googleOpts = builder.Configuration.GetSection(GoogleAuthOptions.SectionName).Get<GoogleAuthOptions>() ?? new();

var authenticationBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
});

authenticationBuilder.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtOpts.Issuer,
        ValidAudience = jwtOpts.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOpts.SigningKey))
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                context.Token = accessToken;
            return Task.CompletedTask;
        }
    };
});

authenticationBuilder.AddCookie("External", cookie =>
{
    cookie.Cookie.Name = "vc.external";
    cookie.ExpireTimeSpan = TimeSpan.FromMinutes(15);
    cookie.SlidingExpiration = true;
});

if (googleOpts.IsConfigured)
{
    authenticationBuilder.AddGoogle("Google", google =>
    {
        google.SignInScheme = "External";
        google.ClientId = googleOpts.ClientId;
        google.ClientSecret = googleOpts.ClientSecret;
        google.CallbackPath = "/signin-google";
    });
}

builder.Services.AddAuthorization();

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
var webClientPublicOrigin = builder.Configuration[$"{WebClientOptions.SectionName}:{nameof(WebClientOptions.PublicOrigin)}"];
builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p =>
        p.SetIsOriginAllowed(origin => WebOriginResolver.IsAllowedCorsOrigin(origin, corsOrigins, webClientPublicOrigin))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

var app = builder.Build();

{
    var emailLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Email");
    var smtpPwd = builder.Configuration["Email:SmtpPassword"];
    if (string.IsNullOrWhiteSpace(smtpPwd))
    {
        emailLog.LogWarning(
            "Email:SmtpPassword is empty. Gmail requires a 16-character App Password (Google Account → Security → App passwords), not your normal Gmail password. " +
            "Set with: dotnet user-secrets set \"Email:SmtpPassword\" \"YOUR_APP_PASSWORD\" (and set Email:SmtpUser / Email:FromAddress to your Gmail). " +
            "In Development, OTPs are logged to the console when SMTP is not configured.");
    }
}


if (app.Environment.IsDevelopment())
{
    var googleAuthForLog = app.Services.GetRequiredService<IOptions<GoogleAuthOptions>>().Value;
    if (!googleAuthForLog.IsConfigured)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("GoogleOAuth");
        log.LogInformation(
            "Google sign-in is not configured. Set GoogleCredentials__ClientId and GoogleCredentials__ClientSecret in .env (see .env.example) or environment variables. " +
            "Authorized redirect URI in Google Cloud: http://localhost:5292/signin-google");
    }
}

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "VoiceChat API v1");
    options.RoutePrefix = "swagger";
});

// CORS must run between UseRouting and endpoint mapping so preflight + credentialed requests get headers.
app.UseRouting();
app.UseCors();

// Only enable HTTPS redirection when this process actually exposes HTTPS (see launchSettings applicationUrl).
// Using the "http" profile (http://localhost:5292 only) would otherwise log:
// "Failed to determine the https port for redirect."
var urls =
    builder.Configuration["ASPNETCORE_URLS"]
    ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
    ?? string.Empty;
if (urls.Contains("https://", StringComparison.OrdinalIgnoreCase))
    app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Database");
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DatabaseMigrator.ApplyPendingMigrationsAsync(db, logger);
}

app.Run();
