# TheUtils.Db.Postgres

Postgres-specific extensions for TheUtils.Db.

## Features

- **COPY Protocol**: High-performance bulk data import/export
- **LISTEN/NOTIFY**: Real-time notifications
- **Advisory Locks**: Distributed locking
- **JSONB Queries**: Native JSONB path queries
- **Raw Npgsql Access**: Direct NpgsqlDataReader queries

## Usage

```csharp
using TheUtils;

var env = DbEnv.FromContext(dbContext);

// Bulk import using COPY protocol
var count = await PostgresDb.binaryImport(
    "users (name, email)",
    users,
    (writer, user) => {
        writer.Write(user.Name, NpgsqlDbType.Text);
        writer.Write(user.Email, NpgsqlDbType.Text);
    }
).Run(env).RunAsync();

// Advisory locks
var result = await PostgresDb.withAdvisoryLock(
    lockKey,
    from u in Db.head(users.Where(x => x.Id == id))
    select u
).Run(env).RunAsync();

// LISTEN/NOTIFY
await PostgresDb.listen("my_channel").Run(env).RunAsync();
await PostgresDb.notify("my_channel", "payload").Run(env).RunAsync();
```

## Installation

```bash
dotnet add package TheUtils.Db.Postgres
```

Requires `TheUtils.Db` (installed automatically as dependency).
