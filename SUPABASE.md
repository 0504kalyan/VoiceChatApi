# Supabase + VoiceChat.Api (end to end)

This API uses **PostgreSQL** through **EF Core + Npgsql**. Table and column names are **snake_case** in the database (`users`, `user_id`, `is_active`, …) via `EFCore.NamingConventions`. [Supabase](https://supabase.com) provides a managed Postgres instance. Follow these steps from zero to a working connection string wired into the app.

The committed `appsettings.json` includes your Supabase **host** with **`Password=` empty** — set the real password via **User Secrets**, **`.env`**, or **environment variables** (`SupabaseCredentials__ConnectionString`), never in git.

## 1. Create a Supabase account and project

1. Open [https://supabase.com](https://supabase.com) and sign up (e.g. **Continue with GitHub**).
2. **New project** → choose **Organization**, **Name**, **Database password** (save it securely), **Region** (closest to your users).
3. Wait until the project status is **Healthy / Active**.

## 2. Get the database connection string

1. In the Supabase dashboard, open your project.
2. Go to **Project Settings** (gear) → **Database**.
3. Under **Connection string**, choose the **URI** or **.NET** style and copy it. You need these parts:

   - **Host** (e.g. `db.<project-ref>.supabase.co` or a **pooler** host like `aws-0-...pooler.supabase.com`)
   - **Port** — direct Postgres is often **5432**; the **transaction pooler** often uses **6543** (see Supabase docs for your chosen mode).
   - **Database** — usually `postgres`.
   - **User** — usually `postgres`.
   - **Password** — the database password you set when creating the project.

4. Build an **Npgsql** connection string (what this API expects), for example:

   ```text
   Host=db.YOUR_PROJECT_REF.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=YOUR_DB_PASSWORD;SSL Mode=Require;Trust Server Certificate=true
   ```

   Adjust **Host**, **Port**, and flags to match the **Session** vs **Pool** connection Supabase shows for your plan. If IPv6 causes issues on your network, Supabase documents **IPv4 add-on** or pooler workarounds.

## 3. Declare the connection string for this API

Configuration is merged from `appsettings*.json`, then **environment variables** (Render, Docker, etc.).

### Option A — `SupabaseCredentials` (matches `appsettings.json` placeholders)

- **Environment variable name:** `SupabaseCredentials__ConnectionString`
- **Value:** the full Npgsql connection string from step 2.

This maps to the `SupabaseCredentials:ConnectionString` section. `ConnectionStrings:DefaultConnection` is filled via `{{SupabaseCredentials:ConnectionString}}` after placeholder expansion.

### Option B — Standard ASP.NET Core connection string

- **Environment variable name:** `ConnectionStrings__DefaultConnection`
- **Value:** the same Npgsql string.

This overrides `ConnectionStrings:DefaultConnection` directly.

### Local development

1. Copy `VoiceChat.Api/.env.example` to `VoiceChat.Api/.env`.
2. Set `SupabaseCredentials__ConnectionString=` to your Supabase string **or** use the default in `appsettings.Development.json` for a **local Postgres** (Docker) with user/password `postgres`/`postgres`.

3. Run the API with `ASPNETCORE_ENVIRONMENT=Development` (default for `dotnet run` from the project).

### Render (or any host)

1. **Dashboard** → your **Web Service** → **Environment**.
2. Add **`SupabaseCredentials__ConnectionString`** or **`ConnectionStrings__DefaultConnection`** (one is enough).
3. Add **`Jwt__SigningKey`** (random string, at least 32 characters).
4. Save and **Deploy**.

Startup logs a **non-secret** line: `PostgreSQL: Host=...; Port=...; Database=...; SSL=...`.

## 4. Create tables (EF Core migrations)

On first deploy, the API runs **`Database.Migrate()`** on startup and applies migrations under `Data/Migrations/`.

To add a migration locally (after model changes):

```bash
cd Api/VoiceChat.Api
dotnet ef migrations add YourMigrationName --output-dir Data/Migrations
```

Design-time defaults use `AppDbContextFactory` (local Postgres URL); adjust if needed.

## 5. Web UI (VoiceChat.Web)

The Angular app does **not** connect to Supabase directly for this API’s data; it calls the **HTTP + SignalR** endpoints on VoiceChat.Api. Point the web app’s API base URL to your deployed API (e.g. Render URL). No Supabase client key is required in the browser for this database path unless you add Supabase features (Auth/Storage) separately.

---

**Summary:** Sign up → create project → copy DB host/user/password → form an **Npgsql** connection string → set **`SupabaseCredentials__ConnectionString`** or **`ConnectionStrings__DefaultConnection`** in `.env` (local) or Render (production) → deploy; migrations apply automatically on startup.
