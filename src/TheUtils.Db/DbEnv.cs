#nullable enable
namespace TheUtils;

using System.Data;
using LanguageExt;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Read-only environment for database monad operations.
/// Contains connection configuration and defaults.
/// </summary>
public record DbEnv(
    DbContext Context,
    Option<IsolationLevel> DefaultIsolation = default,
    Option<TimeSpan> CommandTimeout = default
)
{
    /// <summary>
    /// Creates environment from just a DbContext (most common case).
    /// </summary>
    public static DbEnv FromContext(DbContext context) => new(context);
}
