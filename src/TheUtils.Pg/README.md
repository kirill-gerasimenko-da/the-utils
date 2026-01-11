# TheUtils.Pg

A PostgreSQL monad for functional database operations using [language-ext](https://github.com/louthy/language-ext) v5.

## Overview

`TheUtils.Pg` provides a first-class `Pg<A>` monad that wraps `ReaderT<PgEnv, IO, A>`, giving you:

- **Composable database operations** using LINQ query syntax
- **Environment-based configuration** via `PgEnv`
- **Full EF Core integration** for queries and entity operations
- **Automatic transaction management** via EF Core's `Database.CurrentTransaction`
- **Npgsql-specific features**: COPY protocol, LISTEN/NOTIFY, advisory locks

## Installation

```bash
dotnet add package TheUtils.Pg
```

## Quick Start

```csharp
using TheUtils;
using static TheUtils.Pg;

// Create environment with your DbContext
var env = new PgEnv(dbContext);

// Define a computation
Pg<User> getUser(int id) =>
    from users in set<User>()
    from user in require(users.Where(u => u.Id == id))
    select user;

// Run it
var result = await getUser(42).Run(env).RunAsync();
```

## Core Concepts

### The Pg Monad

`Pg<A>` is a monad that combines:
- **Reader** (`PgEnv`) - read-only environment with DbContext and configuration
- **IO** - async effects

```csharp
// Pg<A> wraps this transformer:
ReaderT<PgEnv, IO, A>
```

### PgEnv (Environment)

Read-only configuration passed to all operations:

```csharp
public record PgEnv(
    DbContext Context,
    Option<NpgsqlConnection> RawConnection = default,
    IsolationLevel DefaultIsolation = IsolationLevel.ReadCommitted,
    Option<TimeSpan> CommandTimeout = default
);

// Create from DbContext
var env = new PgEnv(myDbContext);

// Or with raw Npgsql connection for advanced features
var env = PgEnv.FromConnection(npgsqlConnection, myDbContext);
```

## Usage Examples

### Query Operations

```csharp
using static TheUtils.Pg;

// Get all entities
Pg<Seq<User>> getAllUsers() =>
    from users in set<User>()
    from result in seq(users.Where(u => u.IsActive))
    select result;

// Get single entity (Option)
Pg<Option<User>> findUser(string email) =>
    from users in set<User>()
    from user in head(users.Where(u => u.Email == email))
    select user;

// Require entity (fails if not found)
Pg<User> getUser(string email) =>
    from users in set<User>()
    from user in require(users.Where(u => u.Email == email))
    select user;

// Get single entity with OptionT (for monad transformer chaining)
Pg<Option<string>> getUserEmail(int id) =>
    from result in (
        from users in set<User>()
        from user in headT(users.Where(u => u.Id == id))
        select user.Email
    ).Run()
    select result;

// Check existence
Pg<bool> userExists(string email) =>
    from users in set<User>()
    from exists in any(users.Where(u => u.Email == email))
    select exists;

// Count
Pg<int> countActiveUsers() =>
    from users in set<User>()
    from count in count(users.Where(u => u.IsActive))
    select count;

// Raw SQL query
Pg<Seq<UserDto>> searchUsers(string term) =>
    from results in seq<UserDto>($"SELECT id, name FROM users WHERE name LIKE {term}")
    select results;
```

### Entity Operations

```csharp
// Add entity
Pg<User> createUser(string name, string email) =>
    from entry in add(new User { Name = name, Email = email })
    from _ in saveChanges
    select entry.Entity;

// Update entity
Pg<Unit> updateUserName(User user, string newName) =>
    pure(user.Name = newName)
        .Bind(_ => update(user))
        .Bind(_ => saveChanges);

// Delete entity
Pg<Unit> deleteUser(User user) =>
    delete(user).Bind(_ => saveChanges);

// Bulk operations
Pg<Unit> addUsers(Seq<User> users) =>
    addRange(users).Bind(_ => saveChanges);
```

### Transactions

```csharp
// Automatic transaction with rollback on error
Pg<Unit> transferFunds(int fromId, int toId, decimal amount) =>
    transact(
        from sender in single(set<Account>().Bind(a =>
            head(a.Where(x => x.Id == fromId))))
        from receiver in single(set<Account>().Bind(a =>
            head(a.Where(x => x.Id == toId))))
        from _ in pure(sender.Balance -= amount)
        from __ in pure(receiver.Balance += amount)
        from ___ in saveChanges
        select unit
    );

// Manual transaction control
Pg<Unit> manualTransaction() =>
    from _ in beginTransaction(Some(IsolationLevel.Serializable))
    from __ in execute($"UPDATE accounts SET balance = balance + 100")
    from ___ in commit
    select unit;

// Check current transaction
Pg<bool> isInTransaction() =>
    from tx in currentTransaction
    select tx.IsSome;
```

### Raw SQL Execution

```csharp
// Execute with affected row count
Pg<int> deactivateOldUsers(DateTime cutoff) =>
    execute($"UPDATE users SET is_active = false WHERE last_login < {cutoff}");

// Execute raw SQL with parameters
Pg<int> updateBalances(decimal amount, Seq<int> userIds) =>
    executeRaw("UPDATE users SET balance = balance + @p0 WHERE id = ANY(@p1)",
        Seq<object>(amount, userIds.ToArray()));
```

### Npgsql-Specific Features

#### COPY Protocol (Bulk Import)

```csharp
// Bulk import using PostgreSQL COPY
Pg<ulong> bulkImportUsers(Seq<User> users) =>
    binaryImport("users (name, email, balance)", users, (writer, user) =>
    {
        writer.Write(user.Name, NpgsqlDbType.Text);
        writer.Write(user.Email, NpgsqlDbType.Text);
        writer.Write(user.Balance, NpgsqlDbType.Numeric);
    });
```

#### LISTEN/NOTIFY (Pub/Sub)

```csharp
// Subscribe to notifications
Pg<Unit> subscribeToChanges() =>
    from _ in listen("user_changes")
    from notifications in notifications
    // Process notifications...
    select unit;

// Send notification
Pg<Unit> notifyChange(string payload) =>
    notify("user_changes", payload);
```

#### Advisory Locks (Distributed Locking)

```csharp
// Scoped lock - guarantees release on completion/error
Pg<Unit> processWithLock(long resourceId) =>
    withAdvisoryLock(resourceId,
        from data in loadData(resourceId)
        from _ in processData(data)
        from __ in saveResults(data)
        select unit
    );

// Manual lock control
Pg<bool> tryProcessResource(long id) =>
    from acquired in tryAdvisoryLock(id)
    from _ in acquired
        ? processResource(id).Bind(_ => advisoryUnlock(id))
        : pure(unit)
    select acquired;
```

### Running Computations

```csharp
var env = new PgEnv(dbContext);

// Run and get result
var user = await getUser(42).Run(env).RunAsync();

// Run with cancellation token
var result = await computation.Run(env).RunAsync(cancellationToken);
```

## Error Handling

```csharp
// Require entity (fails with default error if not found)
Pg<User> requireUser(int id) =>
    from users in set<User>()
    from user in require(users.Where(u => u.Id == id))
    select user;

// Require entity with custom error
Pg<User> requireUserWithError(int id) =>
    from users in set<User>()
    from user in require(users.Where(u => u.Id == id), Error.New($"User {id} not found"))
    select user;

// Catch and handle errors
Pg<int> safeOperation() =>
    Pg.Catch(
        riskyOperation(),
        err => err.Message.Contains("timeout"),
        err => pure(0) // fallback value
    ).As();
```

## API Reference

### Query Operations
| Method | Description |
|--------|-------------|
| `seq<A>(IQueryable<A>)` | Execute query, return `Seq<A>` |
| `head<A>(IQueryable<A>)` | First or None (`Pg<Option<A>>`) |
| `headT<A>(IQueryable<A>)` | First as OptionT (for monad transformer chaining) |
| `require<A>(IQueryable<A>, Option<Error>)` | First or fail with custom error |
| `single<A>(IQueryable<A>)` | Exactly one (throws if not) |
| `any<A>(IQueryable<A>)` | Existence check |
| `count<A>(IQueryable<A>)` | Row count |
| `set<A>()` | Get `DbSet<A>` |
| `query<A>(FormattableString)` | Create `IQueryable` from SQL |

### Entity Operations
| Method | Description |
|--------|-------------|
| `add<A>(A)` | Add entity |
| `addRange<A>(Seq<A>)` | Add multiple entities |
| `update<A>(A)` | Update entity |
| `delete<A>(A)` | Remove entity |
| `saveChanges` | Persist changes |

### Transaction Operations
| Method | Description |
|--------|-------------|
| `currentTransaction` | Get current transaction (`Option<IDbContextTransaction>`) |
| `beginTransaction(Option<IsolationLevel>)` | Start transaction |
| `commit` | Commit transaction |
| `rollback` | Rollback transaction |
| `transact<A>(Pg<A>)` | Auto commit/rollback wrapper |

### Npgsql Operations
| Method | Description |
|--------|-------------|
| `binaryImport<A>(...)` | COPY FROM STDIN |
| `listen(channel)` | Subscribe to notifications |
| `notify(channel, payload)` | Send notification |
| `advisoryLock(key)` | Acquire advisory lock |
| `withAdvisoryLock<A>(key, Pg<A>)` | Scoped advisory lock |

## License

MIT License - see [LICENSE](../../LICENSE) for details.
