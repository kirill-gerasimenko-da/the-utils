#nullable enable
namespace TheUtils;

using System.Data;
using System.Data.Common;
using LanguageExt;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Read-only environment for database monad operations.
/// Contains connection configuration and defaults.
/// </summary>
public record DbEnv(
    DbContext Context,
    Option<DbConnection> RawConnection = default,
    Option<IsolationLevel> DefaultIsolation = default,
    Option<TimeSpan> CommandTimeout = default
)
{
    /// <summary>
    /// Gets the database connection, either from explicit RawConnection or from EF Core context.
    /// </summary>
    public DbConnection Connection =>
        RawConnection.IfNone(() => Context.Database.GetDbConnection());

    /// <summary>
    /// Creates environment from just a DbContext (most common case).
    /// </summary>
    public static DbEnv FromContext(DbContext context) => new(context);

    /// <summary>
    /// Creates environment with a dedicated connection for advanced features.
    /// </summary>
    public static DbEnv FromConnection(DbContext context, DbConnection connection) =>
        new(context, connection);
}
