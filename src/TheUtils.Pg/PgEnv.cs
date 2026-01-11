#nullable enable
namespace TheUtils;

using System.Data;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Npgsql;

/// <summary>
/// Read-only environment for PostgreSQL monad operations.
/// Contains connection configuration and defaults.
/// </summary>
public record PgEnv(
    DbContext Context,
    Option<NpgsqlConnection> RawConnection = default,
    Option<IsolationLevel> DefaultIsolation = default,
    Option<TimeSpan> CommandTimeout = default
)
{
    /// <summary>
    /// Gets the Npgsql connection, either from explicit RawConnection or from EF Core context.
    /// </summary>
    public NpgsqlConnection Connection =>
        RawConnection.IfNone(() => (NpgsqlConnection)Context.Database.GetDbConnection());

    /// <summary>
    /// Creates environment from just a DbContext (most common case).
    /// </summary>
    public static PgEnv FromContext(DbContext context) => new(context);

    /// <summary>
    /// Creates environment with a dedicated Npgsql connection for advanced features.
    /// </summary>
    public static PgEnv FromConnection(DbContext context, NpgsqlConnection connection) =>
        new(context, connection);
}
