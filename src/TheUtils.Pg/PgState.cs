namespace TheUtils;

using LanguageExt;
using Microsoft.EntityFrameworkCore.Storage;

/// <summary>
/// Mutable state tracked through PostgreSQL monad computations.
/// Tracks transactions and operation metrics.
/// </summary>
public record PgState(
    Option<IDbContextTransaction> Transaction = default,
    Option<int> OperationCount = default
)
{
    /// <summary>
    /// Default initial state with no transaction.
    /// </summary>
    public static readonly PgState Initial = new();

    /// <summary>
    /// Whether a transaction is currently active.
    /// </summary>
    public bool HasTransaction => Transaction.IsSome;

    /// <summary>
    /// Gets the operation count, defaulting to 0 if not set.
    /// </summary>
    public int Ops => OperationCount.IfNone(0);

    /// <summary>
    /// Increment operation count.
    /// </summary>
    public PgState IncrementOps() => this with { OperationCount = Ops + 1 };

    /// <summary>
    /// Clear the transaction reference.
    /// </summary>
    public PgState ClearTransaction() => this with { Transaction = Option<IDbContextTransaction>.None };
}
