# TheUtils.Cli

Functional wrapper for [CliWrap](https://github.com/Tyrrrz/CliWrap) using [LanguageExt](https://github.com/louthy/language-ext) IO monad. Simple static API for command execution with full CliWrap feature support.

## Features

- ✅ Simple `IO<A>` based API - no complex monad stacks
- ✅ Full CliWrap feature support - buffered, streaming, cancellation, validation, credentials
- ✅ Multiple streaming options - IAsyncEnumerable, IObservable, and SourceT monad-transformer
- ✅ Easy composition with other monads (especially `Db<A>`)
- ✅ LanguageExt integration - `Seq`, `Map`, `Option` types
- ✅ Immutable and functional - no side effects until `RunAsync()`
- ✅ Comprehensive XML documentation

## Installation

```bash
dotnet add package TheUtils.Cli
```

## Quick Start

```csharp
using TheUtils;
using static LanguageExt.Prelude;

// Simple execution
var result = await Cli.execute("echo", ["Hello, World!"])
    .RunAsync();

Console.WriteLine($"Exit code: {result.ExitCode}");
Console.WriteLine($"Success: {result.IsSuccess}");

// Get stdout as string
var output = await Cli.executeToString("ls", ["-la"])
    .RunAsync();

Console.WriteLine(output);
```

## Basic Usage

### Execute Command

```csharp
// Standard execution - returns CommandResult
var result = await Cli.execute("git", ["status"])
    .RunAsync();

// Execute and get stdout
var output = await Cli.executeToString("echo", ["Hello"])
    .RunAsync();

// Buffered execution - captures stdout and stderr separately
var buffered = await Cli.executeBuffered("npm", ["test"])
    .RunAsync();

Console.WriteLine($"Stdout: {buffered.StandardOutput}");
Console.WriteLine($"Stderr: {buffered.StandardError}");
```

### Working Directory and Environment Variables

```csharp
// Set working directory
var output = await Cli.executeToString(
        "pwd",
        workingDirectory: "/tmp"
    )
    .RunAsync();

// Set environment variables
var result = await Cli.execute(
        "node",
        ["app.js"],
        environmentVariables: Map(
            ("NODE_ENV", "production"),
            ("PORT", "3000")
        )
    )
    .RunAsync();
```

### Validation Control

```csharp
// Default: throws on non-zero exit code
try
{
    await Cli.execute("false").RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Command failed: {ex.Message}");
}

// Disable validation - allow any exit code
var result = await Cli.execute(
        "false",
        validation: CommandResultValidation.None
    )
    .RunAsync();

Console.WriteLine($"Exit code: {result.ExitCode}"); // 1 (but no exception)
```

### Piping

```csharp
using CliWrap;

// Pipe string to stdin
var result = await Cli.execute(
        "grep",
        ["hello"],
        standardInput: PipeSource.FromString("hello world\ngoodbye world")
    )
    .RunAsync();

// Pipe stdout to file
var result = await Cli.execute(
        "ls",
        ["-la"],
        standardOutput: PipeTarget.ToFile("output.txt")
    )
    .RunAsync();

// Pipe stdout line-by-line to handler
var result = await Cli.execute(
        "tail",
        ["-f", "log.txt"],
        standardOutput: PipeTarget.ToDelegate(line => Console.WriteLine($"LOG: {line}"))
    )
    .RunAsync();
```

## Advanced Features

### Event Streams

For real-time command output processing:

```csharp
// Pull-based event stream (with back pressure)
var eventsIO = await Cli.executeStream("npm", ["install"])
    .RunAsync();

await foreach (var evt in eventsIO)
{
    switch (evt)
    {
        case StartedCommandEvent started:
            Console.WriteLine($"Started PID: {started.ProcessId}");
            break;

        case StandardOutputCommandEvent output:
            Console.WriteLine($"OUT: {output.Text}");
            break;

        case StandardErrorCommandEvent error:
            Console.WriteLine($"ERR: {error.Text}");
            break;

        case ExitedCommandEvent exited:
            Console.WriteLine($"Exited with code: {exited.ExitCode}");
            break;
    }
}
```

### Advanced Streaming with SourceT

For advanced functional streaming scenarios, TheUtils.Cli provides `executeSourceT()` which returns a `SourceT<IO, CommandEvent>` monad-transformer from LanguageExt.Streaming.

#### When to use SourceT vs IAsyncEnumerable

**Use `executeStream()` (IAsyncEnumerable) when:**
- You want simple, familiar streaming with `await foreach`
- Sequential event processing is sufficient
- You're new to functional programming
- You want minimal dependencies

**Use `executeSourceT()` when:**
- You need advanced stream composition
- You want to integrate with LanguageExt Pipes
- You're building complex functional pipelines
- You need monad-transformer capabilities

#### SourceT Examples

```csharp
using LanguageExt;
using LanguageExt.Streaming;
using static TheUtils.Cli;

// Execute and fold over events
var count = await executeSourceT("echo", ["hello"])
    .Fold(0, (acc, _) => acc + 1)
    .RunAsync();

Console.WriteLine($"Total events: {count}");

// Filter and map events
var outputs = await executeSourceT("ls", ["-la"])
    .Filter(evt => evt is StandardOutputCommandEvent)
    .Map(evt => ((StandardOutputCommandEvent)evt).Text)
    .Fold(List<string>(), (list, text) => list.Add(text))
    .RunAsync();

foreach (var line in outputs)
{
    Console.WriteLine(line);
}

// Take only first N events
var firstTwoEvents = await executeSourceT("echo", ["test"])
    .Take(2)
    .Fold(List<CommandEvent>(), (list, evt) => list.Add(evt))
    .RunAsync();

// Compose SourceT with other IO operations
var result = await (
    from _ in IO.lift(() => Console.WriteLine("Starting..."))
    from sourceT in IO.pure(executeSourceT("echo", ["data"]))
    from events in sourceT.Fold(List<CommandEvent>(), (list, evt) => list.Add(evt))
    select events.Count
).RunAsync();
```

#### SourceT Operations

SourceT supports rich stream operations:
- `Map` - transform events
- `Filter` - filter events by predicate
- `Fold` - reduce stream to single value
- `Take` / `Skip` - limit or skip events
- `Bind` - monadic composition
- Conversion to `Producer` / `ProducerT` for Pipes

See [LanguageExt.Streaming documentation](https://github.com/louthy/language-ext) for complete API reference.

### Cancellation

```csharp
using var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromSeconds(5));

try
{
    var result = await Cli.execute("sleep", ["30"])
        .RunAsync(cts.Token);
}
catch (TaskCanceledException)
{
    Console.WriteLine("Command cancelled after 5 seconds");
}
```

For graceful + forceful cancellation:

```csharp
using var gracefulCts = new CancellationTokenSource();
using var forcefulCts = new CancellationTokenSource();

gracefulCts.CancelAfter(TimeSpan.FromSeconds(5));
forcefulCts.CancelAfter(TimeSpan.FromSeconds(10));

var result = await Cli.executeWithCancellation(
        gracefulCts.Token,  // Try graceful first
        forcefulCts.Token,  // Force kill if needed
        "long-running-process",
        ["--option"]
    )
    .RunAsync();
```

## Composition with Other Monads

### Compose with Db<A>

```csharp
using TheUtils;

// Mix CLI and database operations in LINQ expression
var pipeline =
    from users in Db.seq(db.Users.AsQueryable())
    from count in Db.pure(users.Count)
    from _ in Db.liftIO(
        Cli.executeToString("echo", [$"Processing {count} users"])
    )
    from processed in processUsers(users)
    select processed;

var result = await pipeline
    .Run(new DbRT(dbContext))
    .RunAsync();
```

### LINQ Composition

```csharp
// Chain multiple commands
var output = await (
    from result1 in Cli.executeToString("echo", ["step 1"])
    from result2 in Cli.executeToString("echo", ["step 2"])
    from result3 in Cli.executeToString("echo", ["step 3"])
    select (result1, result2, result3)
).RunAsync();
```

## Extension Methods

The package includes convenient extension methods:

```csharp
// Execute and get stdout
var output = await "ls".executeString("-la");
```

## Accessing Raw CliWrap Command

For advanced scenarios, get the raw CliWrap `Command`:

```csharp
using CliWrap;

var cmd = Cli.command(
    "git",
    ["clone", "https://github.com/user/repo.git"],
    workingDirectory: "/tmp"
);

// Now use CliWrap's API directly
var result = await cmd
    .WithValidation(CommandResultValidation.None)
    .ExecuteAsync();
```

## Migration from TheUtils 1.x

### Before (TheUtils.Cli in TheUtils package)

```csharp
using TheUtils;

// Old Eff-based API
var output = await Cli.newCommand("echo", ["hello"])
    .Bind(Cli.runCommand)
    .Map(r => r.StandardOutput)
    .Run()
    .RunAsync();

// Or convenience method
var output = await Cli.runCommand("echo", ["hello"])
    .Run()
    .RunAsync();
```

### After (TheUtils.Cli package)

```csharp
using TheUtils;

// New IO-based API - simpler and more direct
var output = await Cli.executeToString("echo", ["hello"])
    .RunAsync();

// Or get full result
var result = await Cli.executeBuffered("echo", ["hello"])
    .RunAsync();
var output = result.StandardOutput;
```

### Key Changes

1. **Changed from Eff to IO**: More standard, better performance, simpler API
2. **Simpler execution**: Direct methods instead of build + execute pattern
3. **More features**: Buffered execution, event streams, better validation, credentials
4. **No default pipe targets**: CliWrap defaults are used (captured streams)
5. **Better parameter names**: `workingDirectory` instead of `workingDirPath`
6. **Full CliWrap parity**: Access to all CliWrap features

## Requirements

- .NET 10.0 or later
- LanguageExt.Core 5.0.0-beta-77 or later
- LanguageExt.Streaming 5.0.0-beta-77 or later (for SourceT support)
- CliWrap 3.10.0 or later

## License

MIT

## Links

- [GitHub Repository](https://github.com/kirill-gerasimenko-da/the-utils)
- [CliWrap Documentation](https://github.com/Tyrrrz/CliWrap)
- [LanguageExt Documentation](https://github.com/louthy/language-ext)
