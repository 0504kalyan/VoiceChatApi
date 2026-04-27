using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoiceChat.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnsureIdeTablesExist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS ide_workspaces (
                    id uuid NOT NULL,
                    user_id uuid NOT NULL,
                    name character varying(200) NOT NULL,
                    normalized_name character varying(200) NOT NULL DEFAULT '',
                    created_at timestamp with time zone NOT NULL DEFAULT now(),
                    updated_at timestamp with time zone NOT NULL DEFAULT now(),
                    is_active boolean NOT NULL,
                    CONSTRAINT pk_ide_workspaces PRIMARY KEY (id)
                );

                CREATE TABLE IF NOT EXISTS ide_files (
                    id uuid NOT NULL,
                    user_id uuid NOT NULL,
                    workspace_id uuid NOT NULL,
                    path character varying(1024) NOT NULL,
                    normalized_path character varying(1024) NOT NULL DEFAULT '',
                    language character varying(80) NOT NULL,
                    content text NOT NULL,
                    created_at timestamp with time zone NOT NULL DEFAULT now(),
                    updated_at timestamp with time zone NOT NULL DEFAULT now(),
                    is_active boolean NOT NULL,
                    CONSTRAINT pk_ide_files PRIMARY KEY (id)
                );

                ALTER TABLE ide_workspaces ADD COLUMN IF NOT EXISTS user_id uuid;
                ALTER TABLE ide_workspaces ADD COLUMN IF NOT EXISTS name character varying(200) NOT NULL DEFAULT '';
                ALTER TABLE ide_workspaces ADD COLUMN IF NOT EXISTS normalized_name character varying(200) NOT NULL DEFAULT '';
                ALTER TABLE ide_workspaces ADD COLUMN IF NOT EXISTS created_at timestamp with time zone NOT NULL DEFAULT now();
                ALTER TABLE ide_workspaces ADD COLUMN IF NOT EXISTS updated_at timestamp with time zone NOT NULL DEFAULT now();
                ALTER TABLE ide_workspaces ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;
                UPDATE ide_workspaces
                SET normalized_name = upper(trim(regexp_replace(name, '\s+', ' ', 'g')))
                WHERE normalized_name = '';

                ALTER TABLE ide_files ADD COLUMN IF NOT EXISTS user_id uuid;
                ALTER TABLE ide_files ADD COLUMN IF NOT EXISTS workspace_id uuid;
                ALTER TABLE ide_files ADD COLUMN IF NOT EXISTS path character varying(1024) NOT NULL DEFAULT '';
                ALTER TABLE ide_files ADD COLUMN IF NOT EXISTS normalized_path character varying(1024) NOT NULL DEFAULT '';
                ALTER TABLE ide_files ADD COLUMN IF NOT EXISTS language character varying(80) NOT NULL DEFAULT '';
                ALTER TABLE ide_files ADD COLUMN IF NOT EXISTS content text NOT NULL DEFAULT '';
                ALTER TABLE ide_files ADD COLUMN IF NOT EXISTS created_at timestamp with time zone NOT NULL DEFAULT now();
                ALTER TABLE ide_files ADD COLUMN IF NOT EXISTS updated_at timestamp with time zone NOT NULL DEFAULT now();
                ALTER TABLE ide_files ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;
                UPDATE ide_files AS f
                SET
                    user_id = w.user_id,
                    normalized_path = upper(trim(both '/' from replace(f.path, '\', '/')))
                FROM ide_workspaces AS w
                WHERE f.workspace_id = w.id
                  AND (f.user_id IS NULL OR f.normalized_path = '');

                ALTER TABLE ide_workspaces ALTER COLUMN user_id SET NOT NULL;
                ALTER TABLE ide_files ALTER COLUMN user_id SET NOT NULL;
                ALTER TABLE ide_files ALTER COLUMN workspace_id SET NOT NULL;

                CREATE INDEX IF NOT EXISTS ix_ide_workspaces_user_id_updated_at
                    ON ide_workspaces (user_id, updated_at);

                CREATE INDEX IF NOT EXISTS ix_ide_workspaces_user_id_normalized_name
                    ON ide_workspaces (user_id, normalized_name);

                CREATE INDEX IF NOT EXISTS ix_ide_files_workspace_id_updated_at
                    ON ide_files (workspace_id, updated_at);

                CREATE UNIQUE INDEX IF NOT EXISTS ix_ide_files_user_id_workspace_id_normalized_path
                    ON ide_files (user_id, workspace_id, normalized_path)
                    WHERE is_active = TRUE;

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint WHERE conname = 'fk_ide_workspaces_users_user_id'
                    ) THEN
                        ALTER TABLE ide_workspaces
                        ADD CONSTRAINT fk_ide_workspaces_users_user_id
                        FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint WHERE conname = 'fk_ide_files_users_user_id'
                    ) THEN
                        ALTER TABLE ide_files
                        ADD CONSTRAINT fk_ide_files_users_user_id
                        FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint WHERE conname = 'fk_ide_files_ide_workspaces_workspace_id'
                    ) THEN
                        ALTER TABLE ide_files
                        ADD CONSTRAINT fk_ide_files_ide_workspaces_workspace_id
                        FOREIGN KEY (workspace_id) REFERENCES ide_workspaces (id) ON DELETE CASCADE;
                    END IF;
                END $$;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally no-op. This migration repairs tables that may already exist or contain user data.
        }
    }
}
