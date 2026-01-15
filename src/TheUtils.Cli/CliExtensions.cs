namespace TheUtils;

using LanguageExt;
using static LanguageExt.Prelude;

/// <summary>
/// Extension methods for Cli operations to enable fluent composition.
/// </summary>
public static class CliExtensions
{
    /// <summary>
    /// Execute command and ignore result (return Unit).
    /// Useful for fire-and-forget scenarios or when composing with other monads.
    /// </summary>
    /// <param name="executablePath">Path to the executable to run</param>
    /// <param name="arguments">Command arguments</param>
    /// <returns>IO monad containing Unit</returns>
    public static IO<Unit> executeIgnore(
        this string executablePath,
        params string[] arguments
    ) =>
        Cli.execute(executablePath, toSeq(arguments))
            .Map(_ => unit);

    /// <summary>
    /// Execute command and return stdout as string.
    /// Convenience extension for simple command execution.
    /// </summary>
    /// <param name="executablePath">Path to the executable to run</param>
    /// <param name="arguments">Command arguments</param>
    /// <returns>IO monad containing stdout string</returns>
    public static IO<string> executeString(
        this string executablePath,
        params string[] arguments
    ) =>
        Cli.executeToString(executablePath, toSeq(arguments));
}
