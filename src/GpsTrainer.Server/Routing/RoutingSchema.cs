using GpsTrainer.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GpsTrainer.Server.Routing;

public static class RoutingSchema
{
    public static async Task EnsureAsync(GpsTrainerDb db, CancellationToken cancellationToken = default)
    {
        // EnsureCreated does not add tables to a database that already exists.
        if (db.Database.IsSqlite())
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS RoutingSetting (
                    Id INTEGER NOT NULL CONSTRAINT PK_RoutingSetting PRIMARY KEY,
                    Provider TEXT NOT NULL,
                    ProtectedApiKey TEXT NULL
                );
                """,
                cancellationToken);
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "RoutingSetting" (
                "Id" integer NOT NULL,
                "Provider" text NOT NULL,
                "ProtectedApiKey" text NULL,
                CONSTRAINT "PK_RoutingSetting" PRIMARY KEY ("Id")
            );
            """,
            cancellationToken);
    }
}
