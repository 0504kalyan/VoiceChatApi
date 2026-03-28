using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using VoiceChat.Api.Data;
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

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                  ?? ["http://localhost:4200", "https://localhost:4200"];
builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p =>
        p.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

var app = builder.Build();

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
app.UseHttpsRedirection();
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
    await DatabaseSeed.EnsureDemoUserAsync(db);
}

app.Run();
