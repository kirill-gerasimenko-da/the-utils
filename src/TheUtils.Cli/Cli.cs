// ReSharper disable MemberCanBePrivate.Global

namespace TheUtils;

using System.Text;
using CliWrap;
using CliWrap.Buffered;
using CliWrap.EventStream;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using static LanguageExt.Prelude;

/// <summary>
/// Functional wrapper for CliWrap using LanguageExt IO monad.
/// Provides simple static methods for command execution with full CliWrap feature support.
/// </summary>
public static class Cli
{
    // ==================== Command Building ====================

    /// <summary>
    /// Create a CliWrap Command from parameters.
    /// Returns the raw Command for advanced scenarios or direct usage.
    /// </summary>
    /// <param name="executablePath">Path to the executable to run</param>
    /// <param name="arguments">Command arguments</param>
    /// <param name="workingDirectory">Optional working directory</param>
    /// <param name="environmentVariables">Optional environment variables to set</param>
    /// <param name="credentials">Optional credentials for running as different user</param>
    /// <param name="validation">Exit code validation strategy (default: check for zero)</param>
    /// <param name="standardInput">Optional standard input pipe source</param>
    /// <param name="standardOutput">Optional standard output pipe target</param>
    /// <param name="standardError">Optional standard error pipe target</param>
    /// <returns>Configured CliWrap Command</returns>
    public static Command command(
        string executablePath,
        Seq<string> arguments = default,
        Option<string> workingDirectory = default,
        Map<string, string?> environmentVariables = default,
        Option<Credentials> credentials = default,
        CommandResultValidation validation = CommandResultValidation.ZeroExitCode,
        Option<PipeSource> standardInput = default,
        Option<PipeTarget> standardOutput = default,
        Option<PipeTarget> standardError = default
    )
    {
        var cmd = CliWrap.Cli.Wrap(executablePath);

        if (!arguments.IsEmpty)
            cmd = cmd.WithArguments(arguments.ToArray());

        if (workingDirectory.IsSome)
            cmd = cmd.WithWorkingDirectory(workingDirectory.ValueUnsafe());

        if (!environmentVariables.IsEmpty)
        {
            var envDict = new Dictionary<string, string?>();
            foreach (var kvp in environmentVariables)
                envDict[kvp.Key] = kvp.Value;
            cmd = cmd.WithEnvironmentVariables(envDict);
        }

        if (credentials.IsSome)
            cmd = cmd.WithCredentials(credentials.ValueUnsafe());

        cmd = cmd.WithValidation(validation);

        if (standardInput.IsSome)
            cmd = cmd.WithStandardInputPipe(standardInput.ValueUnsafe());

        if (standardOutput.IsSome)
            cmd = cmd.WithStandardOutputPipe(standardOutput.ValueUnsafe());

        if (standardError.IsSome)
            cmd = cmd.WithStandardErrorPipe(standardError.ValueUnsafe());

        return cmd;
    }

    // ==================== Standard Execution ====================

    /// <summary>
    /// Execute a command and return CommandResult.
    /// </summary>
    /// <param name="executablePath">Path to the executable to run</param>
    /// <param name="arguments">Command arguments</param>
    /// <param name="workingDirectory">Optional working directory</param>
    /// <param name="environmentVariables">Optional environment variables to set</param>
    /// <param name="credentials">Optional credentials for running as different user</param>
    /// <param name="validation">Exit code validation strategy (default: check for zero)</param>
    /// <param name="standardInput">Optional standard input pipe source</param>
    /// <param name="standardOutput">Optional standard output pipe target</param>
    /// <param name="standardError">Optional standard error pipe target</param>
    /// <returns>IO monad containing the CommandResult</returns>
    public static IO<CommandResult> execute(
        string executablePath,
        Seq<string> arguments = default,
        Option<string> workingDirectory = default,
        Map<string, string?> environmentVariables = default,
        Option<Credentials> credentials = default,
        CommandResultValidation validation = CommandResultValidation.ZeroExitCode,
        Option<PipeSource> standardInput = default,
        Option<PipeTarget> standardOutput = default,
        Option<PipeTarget> standardError = default
    ) =>
        IO.liftAsync(async io =>
        {
            var cmd = command(
                executablePath,
                arguments,
                workingDirectory,
                environmentVariables,
                credentials,
                validation,
                standardInput,
                standardOutput,
                standardError
            );

            return await cmd.ExecuteAsync(io.Token);
        });

    /// <summary>
    /// Execute a command with graceful and forceful cancellation tokens.
    /// Graceful cancellation sends a cooperative signal (Ctrl+C equivalent),
    /// while forceful cancellation kills the process.
    /// </summary>
    /// <param name="graceful">Graceful cancellation token (cooperative)</param>
    /// <param name="forceful">Forceful cancellation token (kills process)</param>
    /// <param name="executablePath">Path to the executable to run</param>
    /// <param name="arguments">Command arguments</param>
    /// <param name="workingDirectory">Optional working directory</param>
    /// <param name="environmentVariables">Optional environment variables to set</param>
    /// <param name="credentials">Optional credentials for running as different user</param>
    /// <param name="validation">Exit code validation strategy (default: check for zero)</param>
    /// <param name="standardInput">Optional standard input pipe source</param>
    /// <param name="standardOutput">Optional standard output pipe target</param>
    /// <param name="standardError">Optional standard error pipe target</param>
    /// <returns>IO monad containing the CommandResult</returns>
    public static IO<CommandResult> executeWithCancellation(
        CancellationToken graceful,
        CancellationToken forceful,
        string executablePath,
        Seq<string> arguments = default,
        Option<string> workingDirectory = default,
        Map<string, string?> environmentVariables = default,
        Option<Credentials> credentials = default,
        CommandResultValidation validation = CommandResultValidation.ZeroExitCode,
        Option<PipeSource> standardInput = default,
        Option<PipeTarget> standardOutput = default,
        Option<PipeTarget> standardError = default
    ) =>
        IO.liftAsync(_ =>
        {
            var cmd = command(
                executablePath,
                arguments,
                workingDirectory,
                environmentVariables,
                credentials,
                validation,
                standardInput,
                standardOutput,
                standardError
            );

            return cmd.ExecuteAsync(forceful, graceful).Task;
        });

    // ==================== Buffered Execution ====================

    /// <summary>
    /// Execute a command and return buffered output (stdout and stderr as strings).
    /// </summary>
    /// <param name="executablePath">Path to the executable to run</param>
    /// <param name="arguments">Command arguments</param>
    /// <param name="stdOutEncoding">Optional encoding for stdout (default: UTF8)</param>
    /// <param name="stdErrEncoding">Optional encoding for stderr (default: UTF8)</param>
    /// <param name="workingDirectory">Optional working directory</param>
    /// <param name="environmentVariables">Optional environment variables to set</param>
    /// <param name="credentials">Optional credentials for running as different user</param>
    /// <param name="validation">Exit code validation strategy (default: check for zero)</param>
    /// <param name="standardInput">Optional standard input pipe source</param>
    /// <returns>IO monad containing the BufferedCommandResult with stdout and stderr strings</returns>
    public static IO<BufferedCommandResult> executeBuffered(
        string executablePath,
        Seq<string> arguments = default,
        Option<Encoding> stdOutEncoding = default,
        Option<Encoding> stdErrEncoding = default,
        Option<string> workingDirectory = default,
        Map<string, string?> environmentVariables = default,
        Option<Credentials> credentials = default,
        CommandResultValidation validation = CommandResultValidation.ZeroExitCode,
        Option<PipeSource> standardInput = default
    ) =>
        IO.liftAsync(async io =>
        {
            var cmd = command(
                executablePath,
                arguments,
                workingDirectory,
                environmentVariables,
                credentials,
                validation,
                standardInput
            );

            return await cmd.ExecuteBufferedAsync(
                stdOutEncoding.IfNone(() => null!),
                stdErrEncoding.IfNone(() => null!),
                io.Token
            );
        });

    /// <summary>
    /// Execute a command and return just stdout as a string.
    /// Convenience method for simple cases where only stdout is needed.
    /// </summary>
    /// <param name="executablePath">Path to the executable to run</param>
    /// <param name="arguments">Command arguments</param>
    /// <param name="workingDirectory">Optional working directory</param>
    /// <param name="environmentVariables">Optional environment variables to set</param>
    /// <param name="validation">Exit code validation strategy (default: check for zero)</param>
    /// <param name="standardInput">Optional standard input pipe source</param>
    /// <returns>IO monad containing the stdout string</returns>
    public static IO<string> executeToString(
        string executablePath,
        Seq<string> arguments = default,
        Option<string> workingDirectory = default,
        Map<string, string?> environmentVariables = default,
        CommandResultValidation validation = CommandResultValidation.ZeroExitCode,
        Option<PipeSource> standardInput = default
    ) =>
        from result in executeBuffered(
            executablePath,
            arguments,
            workingDirectory: workingDirectory,
            environmentVariables: environmentVariables,
            validation: validation,
            standardInput: standardInput
        )
        select result.StandardOutput;

    // ==================== Event Stream Execution ====================

    /// <summary>
    /// Execute a command as an event stream (pull-based).
    /// Returns IAsyncEnumerable of CommandEvent for real-time processing with back pressure.
    /// Events include: StartedCommandEvent, StandardOutputCommandEvent, StandardErrorCommandEvent, ExitedCommandEvent.
    /// </summary>
    /// <param name="executablePath">Path to the executable to run</param>
    /// <param name="arguments">Command arguments</param>
    /// <param name="stdOutEncoding">Optional encoding for stdout (default: UTF8)</param>
    /// <param name="stdErrEncoding">Optional encoding for stderr (default: UTF8)</param>
    /// <param name="workingDirectory">Optional working directory</param>
    /// <param name="environmentVariables">Optional environment variables to set</param>
    /// <param name="credentials">Optional credentials for running as different user</param>
    /// <param name="validation">Exit code validation strategy (default: check for zero)</param>
    /// <param name="standardInput">Optional standard input pipe source</param>
    /// <returns>IO monad containing the IAsyncEnumerable of CommandEvent</returns>
    public static IO<IAsyncEnumerable<CommandEvent>> executeStream(
        string executablePath,
        Seq<string> arguments = default,
        Option<Encoding> stdOutEncoding = default,
        Option<Encoding> stdErrEncoding = default,
        Option<string> workingDirectory = default,
        Map<string, string?> environmentVariables = default,
        Option<Credentials> credentials = default,
        CommandResultValidation validation = CommandResultValidation.ZeroExitCode,
        Option<PipeSource> standardInput = default
    ) =>
        IO.lift(() =>
        {
            var cmd = command(
                executablePath,
                arguments,
                workingDirectory,
                environmentVariables,
                credentials,
                validation,
                standardInput
            );

            return cmd.ListenAsync(
                stdOutEncoding.IfNone(() => null!),
                stdErrEncoding.IfNone(() => null!)
            );
        });

    /// <summary>
    /// Execute a command as an observable stream (push-based).
    /// Returns IObservable of CommandEvent compatible with Rx.NET operators.
    /// No back pressure - data flows at process rate.
    /// Requires System.Reactive package for advanced operators.
    /// </summary>
    /// <param name="executablePath">Path to the executable to run</param>
    /// <param name="arguments">Command arguments</param>
    /// <param name="stdOutEncoding">Optional encoding for stdout (default: UTF8)</param>
    /// <param name="stdErrEncoding">Optional encoding for stderr (default: UTF8)</param>
    /// <param name="workingDirectory">Optional working directory</param>
    /// <param name="environmentVariables">Optional environment variables to set</param>
    /// <param name="credentials">Optional credentials for running as different user</param>
    /// <param name="validation">Exit code validation strategy (default: check for zero)</param>
    /// <param name="standardInput">Optional standard input pipe source</param>
    /// <returns>IO monad containing the IObservable of CommandEvent</returns>
    public static IO<IObservable<CommandEvent>> executeObservable(
        string executablePath,
        Seq<string> arguments = default,
        Option<Encoding> stdOutEncoding = default,
        Option<Encoding> stdErrEncoding = default,
        Option<string> workingDirectory = default,
        Map<string, string?> environmentVariables = default,
        Option<Credentials> credentials = default,
        CommandResultValidation validation = CommandResultValidation.ZeroExitCode,
        Option<PipeSource> standardInput = default
    ) =>
        IO.lift(() =>
        {
            var cmd = command(
                executablePath,
                arguments,
                workingDirectory,
                environmentVariables,
                credentials,
                validation,
                standardInput
            );

            return cmd.Observe(
                stdOutEncoding.IfNone(() => null!),
                stdErrEncoding.IfNone(() => null!)
            );
        });

    /// <summary>
    /// Execute command as SourceT stream (functional monad-transformer approach).
    /// Returns SourceT that can be composed with other streams and effects.
    /// Requires understanding of LanguageExt.Streaming - consider using executeStream()
    /// for simpler IAsyncEnumerable-based streaming.
    /// </summary>
    /// <param name="executablePath">Path to the executable to run</param>
    /// <param name="arguments">Command arguments</param>
    /// <param name="stdOutEncoding">Optional encoding for stdout (default: UTF8)</param>
    /// <param name="stdErrEncoding">Optional encoding for stderr (default: UTF8)</param>
    /// <param name="workingDirectory">Optional working directory</param>
    /// <param name="environmentVariables">Optional environment variables to set</param>
    /// <param name="credentials">Optional credentials for running as different user</param>
    /// <param name="validation">Exit code validation strategy (default: check for zero)</param>
    /// <param name="standardInput">Optional standard input pipe source</param>
    /// <returns>SourceT monad-transformer containing command events</returns>
    public static SourceT<IO, CommandEvent> executeSourceT(
        string executablePath,
        Seq<string> arguments = default,
        Option<Encoding> stdOutEncoding = default,
        Option<Encoding> stdErrEncoding = default,
        Option<string> workingDirectory = default,
        Map<string, string?> environmentVariables = default,
        Option<Credentials> credentials = default,
        CommandResultValidation validation = CommandResultValidation.ZeroExitCode,
        Option<PipeSource> standardInput = default
    )
    {
        // Execute the stream and get IO<IAsyncEnumerable<CommandEvent>>
        var streamIO = executeStream(
            executablePath,
            arguments,
            stdOutEncoding,
            stdErrEncoding,
            workingDirectory,
            environmentVariables,
            credentials,
            validation,
            standardInput
        );

        // Lift the IO<IAsyncEnumerable> into SourceT by binding and lifting
        return from stream in SourceT.liftIO<IO, IAsyncEnumerable<CommandEvent>>(streamIO)
               from evt in SourceT.lift<IO, CommandEvent>(stream)
               select evt;
    }
}
