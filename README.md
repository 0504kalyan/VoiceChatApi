# VoiceChat

Angular + .NET AI chat with optional browser speech (Web Speech API). The backend uses **[Ollama](https://ollama.com)** for LLM replies — **runs locally, no paid cloud LLM API** (no OpenAI billing).

## Repository layout

Projects are **siblings at the repository root** (not under `src/`):

| Folder | Description |
|--------|--------------|
| **`VoiceChat.Api/`** | ASP.NET Core, EF Core, SignalR `ChatHub`, `ILlmClient` → local **Ollama** (`/api/chat`). **Docker:** repo-root [`Dockerfile`](../Dockerfile) (context `.`) or [`Api/Dockerfile`](Dockerfile) (context `Api`). Local compose: [`Api/docker-compose.yml`](docker-compose.yml). |
| **`VoiceChat.Web/`** | Angular UI, `@microsoft/signalr`, voice input via the browser |
| **`docs/`** | Architecture notes (`VoiceChat.Api.md`, `VoiceChat.Web.md`) |
| **`VoiceChat.sln`** | **Visual Studio solution** (classic `.sln`) — open this file to load the API project |

### Visual Studio

- Open **`VoiceChat.sln`** at the **repository root** (not a `.slnx` file inside `VoiceChat.Api`). In **.NET 10 SDK**, `dotnet new sln` defaults to **`.slnx`** (XML); older Visual Studio builds or some workloads open that as an empty solution. This repo includes a **`.sln`** (Format Version 12) that loads **`VoiceChat.Api`** reliably.
- The Angular app (**`VoiceChat.Web/`**) is not a C# project; run it with `npm` / `ng serve` or open that folder separately in VS Code / Cursor.

If you still have an old **`src/`** folder with duplicate projects (from before the split), **close Visual Studio / Cursor** so files are not locked, then delete the entire **`src`** directory.

---

## AI backend (Ollama — free local models)

1. **Install [Ollama](https://ollama.com)** for your OS and start it (it listens on `http://localhost:11434` by default).

2. **Pull at least one model** (pick any from the [model library](https://ollama.com/library)):

```bash
ollama pull llama3.2
```

The default in `appsettings.json` is **`llama3.2`**. Change **`Ollama:DefaultModel`** to match what you pulled (e.g. `phi3`, `mistral`, `gemma2`).

3. **Optional configuration** (`VoiceChat.Api/appsettings.json` or environment variables `Ollama__BaseUrl`, `Ollama__DefaultModel`):

- **`Ollama:BaseUrl`** — if Ollama runs on another machine or port (default `http://localhost:11434/`).
- **`Ollama:DefaultModel`** — default tag for new chats.

4. **Check the API**: `GET /api/health/llm` shows the active defaults; `GET /api/health/ollama/models` lists models Ollama has available (like `ollama list`).

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
- **[Ollama](https://ollama.com)** with at least one model pulled

## Run the API

```bash
cd VoiceChat.Api
dotnet run --launch-profile http
```

Database: **SQL Server** via `ConnectionStrings:DefaultConnection` in `appsettings.json` (example: `Server=.;Database=voice_db;Trusted_Connection=True;…`). On startup the app tries to **create the database catalog** if it does not exist (when permitted), then **applies pending EF Core migrations** (see *Database schema* above). Tables are tracked in **`__EFMigrationsHistory`**. Optional manual apply: `dotnet ef database update --project VoiceChat.Api`.

With `dotnet run` or Visual Studio **F5**, the browser opens **Swagger UI** at `/swagger` (Development only). You can also open `http://localhost:5292/swagger` manually.

### Google sign-in (optional)

1. Copy **`VoiceChat.Api/.env.example`** to **`.env`** in the same folder (`.env` is git-ignored).
2. Set **`GoogleCredentials__ClientId`** and **`GoogleCredentials__ClientSecret`** from [Google Cloud Console](https://console.cloud.google.com/) (OAuth 2.0 Client IDs, Web application).
3. Add **Authorized redirect URI**: `http://localhost:5292/signin-google`.
4. Restart the API. In Development, `appsettings.json` uses placeholders `{{GoogleCredentials:ClientId}}` / `{{GoogleCredentials:ClientSecret}}` — real values come from `.env` or environment variables only; do not commit secrets.

## Run the web app

```bash
cd VoiceChat.Web
npm install
npx ng serve
```

Open `http://localhost:4200`. The dev build points the API to `http://localhost:5292` (see `VoiceChat.Web/src/environments/environment.development.ts`).
