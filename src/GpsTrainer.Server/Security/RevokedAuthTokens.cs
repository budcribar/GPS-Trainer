using System.Collections.Concurrent;
using GpsTrainer.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GpsTrainer.Server.Security;

/// <summary>
/// Bearer tokens that sign-out has cancelled. The set is loaded from the database
/// at startup so a restarted process still rejects them until they expire.
/// </summary>
public sealed class RevokedAuthTokens
{
    private readonly ConcurrentDictionary<string, long> _until = new(StringComparer.Ordinal);

    public void Remember(string tokenId, long expiresUnix)
    {
        if (!string.IsNullOrEmpty(tokenId))
            _until[tokenId] = expiresUnix;
    }

    public bool IsRevoked(string? tokenId)
    {
        if (string.IsNullOrEmpty(tokenId) || !_until.TryGetValue(tokenId, out var expires))
            return false;
        if (expires < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            _until.TryRemove(tokenId, out _);
            return false;
        }
        return true;
    }

    public async Task LoadAsync(GpsTrainerDb db, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var rows = await db.RevokedAuthTokens.Where(row => row.ExpiresUnix >= now).ToListAsync(cancellationToken);
        foreach (var row in rows)
            Remember(row.TokenId, row.ExpiresUnix);
        var stale = await db.RevokedAuthTokens.Where(row => row.ExpiresUnix < now).ToListAsync(cancellationToken);
        if (stale.Count == 0)
            return;
        db.RevokedAuthTokens.RemoveRange(stale);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public static class RevokedTokenSchema
{
    public static async Task EnsureAsync(GpsTrainerDb db, CancellationToken cancellationToken = default)
    {
        // EnsureCreated does not add a table to a database that already exists.
        if (db.Database.IsSqlite())
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS RevokedAuthToken (
                    TokenId TEXT NOT NULL CONSTRAINT PK_RevokedAuthToken PRIMARY KEY,
                    ExpiresUnix INTEGER NOT NULL
                );
                """,
                cancellationToken);
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "RevokedAuthToken" (
                "TokenId" text NOT NULL,
                "ExpiresUnix" bigint NOT NULL,
                CONSTRAINT "PK_RevokedAuthToken" PRIMARY KEY ("TokenId")
            );
            """,
            cancellationToken);
    }
}
