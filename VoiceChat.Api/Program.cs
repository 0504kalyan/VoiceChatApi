using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using VoiceChat.Api.Data;
using VoiceChat.Api.Models.Entities;
using VoiceChat.Api.Hubs;
using VoiceChat.Api.Interfaces;
using VoiceChat.Api.Options;
using VoiceChat.Api.Services;

var builder = WebApplication.CreateBuilder(args);

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

var conn = builder.Configuration.GetConnectionString("DefaultConnection")
           ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(conn));

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

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                  ?? ["http://localhost:4200", "https://localhost:4200", "http://localhost:62336"];
builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p =>
        p.WithOrigins(corsOrigins)
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
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "VoiceChat API v1");
        options.RoutePrefix = "swagger";
    });
}

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
    try
    {
        await SqlServerDatabaseBootstrap.EnsureDatabaseExistsAsync(conn);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex,
            "Could not auto-create the database catalog. If the database already exists, migrations will still run.");
    }

    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DatabaseMigrator.ApplyPendingMigrationsAsync(db, logger);
}

app.Run();
