# Supabase + VoiceChat.Api (end to end)

This API uses **PostgreSQL** through **EF Core + Npgsql**. Table and column names are **snake_case** in the database (`users`, `user_id`, `is_active`, …) via `EFCore.NamingConventions`. [Supabase](https://supabase.com) provides a managed Postgres instance. Follow these steps from zero to a working connection string wired into the app.

The committed `appsettings.json` includes your Supabase **host** with **`Password=` empty** — set the real password via **User Secrets**, **`.env`**, or **environment variables** (`SupabaseCredentials__ConnectionString`), never in git.

## 1. Create a Supabase account and project

1. Open [https://supabase.com](https://supabase.com) and sign up (e.g. **Continue with GitHub**).
2. **New project** → choose **Organization**, **Name**, **Database password** (save it securely), **Region** (closest to your users).
3. Wait until the project status is **Healthy / Active**.

## 2. Get the database connection string

1. In the Supabase dashboard, open your project and click **Connect** (or **Project Settings** → **Database**).
2. Prefer the **Session pooler** or **Transaction pooler** connection details when deploying to **Render**, **Railway**, **Fly.io**, or any host that is **IPv4-only**.

### Render / IPv6 (“Network is unreachable”, IPv6 address in logs)

Supabase’s **direct** host `db.<project-ref>.supabase.co` often resolves to **IPv6**. Many clouds (including Render’s free tier) **cannot route IPv6** to the public internet, so Npgsql fails with `SocketException: Network is unreachable` to an IPv6 address.

**Permanent fix (pick one):**

1. **Use the pooler (recommended, no extra cost)**  
   - In Supabase, open **Connect**.  
   - Copy the **Session pooler** connection (host like `aws-0-<REGION>.pooler.supabase.com`, port **5432**).  
   - Username is often `postgres.<PROJECT_REF>` (not only `postgres`) — use exactly what Supabase shows.  
   - Build your Npgsql string from that (same `Password`, `SSL Mode=Require`, etc.).  
   - **EF Core migrations** work best with **Session pooler** or direct DB; avoid **Transaction** mode (port **6543**) for migrations if Supabase docs warn about DDL limitations.

2. **IPv4 add-on (Supabase paid add-on)**  
   - Enables IPv4 for the **direct** `db.*` connection if you need direct access without the pooler.

Do **not** rely on `db.*.supabase.co` alone on IPv4-only hosts unless you have confirmed IPv4 routing (add-on or DNS behavior).

### Example Npgsql strings (shape only — copy values from your **Connect** panel)

Direct (may be IPv6-only):

```text
Host=db.YOUR_PROJECT_REF.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=YOUR_DB_PASSWORD;SSL Mode=Require;Trust Server Certificate=true
```

Session pooler (often IPv4-friendly for Render):

```text
Host=aws-0-YOUR_REGION.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.YOUR_PROJECT_REF;Password=YOUR_DB_PASSWORD;SSL Mode=Require;Trust Server Certificate=true
```

**Required:** `Username` must be `postgres.` plus your **project ref** (from the Supabase URL or Connect panel). Using only `postgres` against a `*.pooler.supabase.com` host fails with `(ENOIDENTIFIER) no tenant identifier`. The API sets `Gss Encryption Mode=Disable` automatically so Docker images without Kerberos libraries still connect.

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
