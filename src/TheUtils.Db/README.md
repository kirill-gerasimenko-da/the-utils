# TheUtils.Db

Database-agnostic monad for functional EF Core operations using language-ext v5.

## Features

- `Db<A>` monad with ReaderT+IO stack
- Full EF Core integration with any database provider
- Transaction management with automatic commit/rollback
- LINQ query syntax support
- Monadic composition for database operations

## Usage

```csharp
using TheUtils;

// Create environment from DbContext
var env = DbEnv.FromContext(dbContext);

// Compose database operations
var operation =
    from user in Db.add(new User { Name = "Alice" })
    from _ in Db.saveChanges
    select user.Entity;

// Run within transaction
var result = await Db.transact(operation)
    .Run(env)
    .RunAsync();
```

## Installation

```bash
dotnet add package TheUtils.Db
```

For PostgreSQL-specific features (COPY, LISTEN/NOTIFY, advisory locks), also add:

```bash
dotnet add package TheUtils.Db.Postgresql
```
