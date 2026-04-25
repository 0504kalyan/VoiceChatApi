# VoiceChat

Angular + .NET AI chat with optional browser speech (Web Speech API). The backend uses **Google Gemini** for LLM replies through the .NET API.

## Repository layout

Projects are **siblings at the repository root** (not under `src/`):

| Folder | Description |
|--------|--------------|
| **`VoiceChat.Api/`** | ASP.NET Core, EF Core, SignalR `ChatHub`, `ILlmClient` → **Gemini Developer API**. **Docker:** repo-root [`Dockerfile`](../Dockerfile) (context `.`) or [`Api/Dockerfile`](Dockerfile) (context `Api`). Local compose: [`Api/docker-compose.yml`](docker-compose.yml). |
| **`VoiceChat.Web/`** | Angular UI, `@microsoft/signalr`, voice input via the browser |
| **`docs/`** | Architecture notes (`VoiceChat.Api.md`, `VoiceChat.Web.md`) |
| **`VoiceChat.sln`** | **Visual Studio solution** (classic `.sln`) — open this file to load the API project |

### Visual Studio

- Open **`VoiceChat.sln`** at the **repository root** (not a `.slnx` file inside `VoiceChat.Api`). In **.NET 10 SDK**, `dotnet new sln` defaults to **`.slnx`** (XML); older Visual Studio builds or some workloads open that as an empty solution. This repo includes a **`.sln`** (Format Version 12) that loads **`VoiceChat.Api`** reliably.
- The Angular app (**`VoiceChat.Web/`**) is not a C# project; run it with `npm` / `ng serve` or open that folder separately in VS Code / Cursor.

If you still have an old **`src/`** folder with duplicate projects (from before the split), **close Visual Studio / Cursor** so files are not locked, then delete the entire **`src`** directory.

---

## AI backend (Gemini Flash)

1. Create an API key in [Google AI Studio](https://aistudio.google.com/app/apikey).

2. Set the key on the API only. Do not put it in Angular/browser files.

```powershell
dotnet user-secrets set "Gemini:ApiKey" "<your Gemini API key>" --project VoiceChat.Api/VoiceChat.Api.csproj
```

For hosted deployments, set:

```text
Gemini__ApiKey=<your Gemini API key>
Gemini__DefaultModel=gemini-2.5-flash
```

3. Model choices configured in `appsettings.json`:

- **`gemini-2.5-flash`** — recommended default; strong price/performance.
- **`gemini-2.5-flash-lite`** — lower cost/latency for simpler chats.
- **`gemini-2.5-pro`** — stronger reasoning/coding, higher cost.
- **`gemini-2.0-flash`** — older Flash alias; keep only if your key/account still supports it.

4. Optional configuration (`VoiceChat.Api/appsettings.json` or environment variables):

- **`Gemini:DefaultModel`** / **`Gemini__DefaultModel`** — default model for new chats.
- **`Gemini:MaxOutputTokens`** / **`Gemini__MaxOutputTokens`** — maximum generated output tokens.
- **`Gemini:MaxHistoryMessages`** / **`Gemini__MaxHistoryMessages`** — number of latest messages sent as context.
- **`Gemini:EnableGoogleSearchGrounding`** / **`Gemini__EnableGoogleSearchGrounding`** — enables Google Search grounding for current/latest/news questions.
- **`Gemini:AvailableModels`** / **`Gemini__AvailableModels__0`** etc. — model dropdown entries.

5. Check the API: `GET /api/health/llm` shows the active defaults; `GET /api/health/gemini/models` lists configured model choices.

### Render environment variables

Set these in Render → your API service → **Environment**:

```text
ASPNETCORE_ENVIRONMENT=Production
PORT=10000

SupabaseCredentials__ConnectionString=<your full PostgreSQL/Npgsql connection string>

Jwt__SigningKey=<random string at least 32 characters>
Jwt__Issuer=VoiceChat.Api
Jwt__Audience=VoiceChat.Web
Jwt__ExpiryMinutes=10080

Gemini__ApiKey=<your Gemini API key>
Gemini__DefaultModel=gemini-2.5-flash
Gemini__MaxOutputTokens=2048
Gemini__MaxHistoryMessages=16
Gemini__EnableGoogleSearchGrounding=true
Gemini__AvailableModels__0=gemini-2.5-flash
Gemini__AvailableModels__1=gemini-2.5-flash-lite
Gemini__AvailableModels__2=gemini-2.5-pro
Gemini__AvailableModels__3=gemini-2.0-flash

Email__SmtpHost=smtp.gmail.com
Email__SmtpPort=587
Email__UseSsl=true
Email__SmtpUser=<your Gmail address>
Email__SmtpPassword=<your 16-character Gmail App Password>
Email__FromAddress=<your Gmail address>
Email__FromName=ChatAI

Google__ClientId=<Google OAuth web client ID>
Google__ClientSecret=<Google OAuth client secret>

WebClient__PublicOrigin=<your deployed Angular app origin>
Cors__Origins__0=<your deployed Angular app origin>

Otp__ExpiryMinutes=10
PasswordReset__ExpiryMinutes=60
```

`SupabaseCredentials__ConnectionString` is enough for the database. Use `ConnectionStrings__DefaultConnection` only if you prefer that name instead.

### Data & UI notes

- **Request + response storage**: Each completed assistant reply is stored in **`RequestResponseArchives`** with **`UserRequest`**, **`ResponseText`**, and **`ResponseJson`** (structured snapshot). Use `GET /api/conversations/{id}/response-archives` to inspect.
- **Soft delete**: `DELETE /api/conversations/{id}` marks the conversation deleted (hidden from lists); rows are retained.
- **Edit message flow**: The UI can remove a user message and everything after it via `DELETE /api/conversations/{conversationId}/messages/{messageId}`, then send again.

### Database schema (automatic apply + how to add tables/columns)

- **On every API start**, the app runs **EF Core `MigrateAsync`** — any **pending** migrations in `VoiceChat.Api/Data/Migrations` are **applied automatically** to SQL Server. Logs will say either “up to date” or list which migrations ran.
- **When you add a new table or column** in C# (`AppDbContext` / entities), you must **generate** a migration once (EF cannot invent migration files at runtime):

  ```bash
  dotnet tool update -g dotnet-ef   # once, if needed
  cd VoiceChat.Api
  dotnet ef migrations add DescribeYourChange --output-dir Data/Migrations
  ```

  Commit the new files under `Data/Migrations`. The **next time you run the API**, startup will apply them automatically — you do not need to run `dotnet ef database update` manually unless you prefer to.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) (LTS)
- Gemini API key from Google AI Studio

## Run the API

```bash
cd VoiceChat.Api
dotnet run --launch-profile http
```

Database: **SQL Server** via `ConnectionStrings:DefaultConnection` in `appsettings.json` (example: `Server=.;Database=voice_db;Trusted_Connection=True;…`). On startup the app tries to **create the database catalog** if it does not exist (when permitted), then **applies pending EF Core migrations** (see *Database schema* above). Tables are tracked in **`__EFMigrationsHistory`**. Optional manual apply: `dotnet ef database update --project VoiceChat.Api`.

With `dotnet run` or Visual Studio **F5**, the browser opens **Swagger UI** at `/swagger` (Development only). You can also open `http://localhost:5292/swagger` manually.

### Google sign-in (optional)

1. Copy **`VoiceChat.Api/.env.example`** to **`.env`** in the same folder (`.env` is git-ignored).
2. Set **`Google__ClientId`** and **`Google__ClientSecret`** from [Google Cloud Console](https://console.cloud.google.com/) (OAuth 2.0 Client IDs, Web application).
3. Add **Authorized redirect URI**: `http://localhost:5292/signin-google`.
4. Restart the API. In Development, `appsettings.json` uses placeholders `{{GoogleCredentials:ClientId}}` / `{{GoogleCredentials:ClientSecret}}` — real values come from `.env` or environment variables only; do not commit secrets.

## Run the web app

```bash
cd VoiceChat.Web
npm install
npx ng serve
```

Open `http://localhost:4200`. The dev build points the API to `http://localhost:5292` (see `VoiceChat.Web/src/environments/environment.development.ts`).
