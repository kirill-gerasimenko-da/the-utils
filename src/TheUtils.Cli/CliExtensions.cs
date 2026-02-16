namespace TheUtils;

using LanguageExt;
using static LanguageExt.Prelude;

/// <summary>
/// Extension methods for Cli operations to enable fluent composition.
/// </summary>
public static class CliExtensions
{
    extension(string executablePath)
    {
        /// <summary>
        /// Execute command and return stdout as string.
        /// Convenience extension for simple command execution.
        /// </summary>
        /// <param name="arguments">Command arguments</param>
        /// <returns>IO monad containing stdout string</returns>
        public IO<string> executeString(params string[] arguments) =>
            Cli.executeToString(executablePath, toSeq(arguments));
    }
}
