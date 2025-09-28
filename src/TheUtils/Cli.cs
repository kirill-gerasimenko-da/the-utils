// ReSharper disable MemberCanBePrivate.Global

namespace TheUtils;

using System.Collections.ObjectModel;
using System.Text;
using CliWrap;
using LanguageExt;
using LanguageExt.Common;
using LanguageExt.Traits;
using LanguageExt.UnsafeValueAccess;
using static LanguageExt.Prelude;

public static class Cli
{
    static readonly PipeTarget defaultOutPipeTarget = PipeTarget.ToDelegate(Console.WriteLine);
    static readonly PipeTarget defaultErrorPipeTarget = PipeTarget.ToDelegate(Console.WriteLine);

    public static Eff<Command> newCommand(
        string executablePath,
        Seq<string> arguments = default,
        Option<string> workingDirPath = default,
        Option<PipeTarget> outPipeTarget = default,
        Option<PipeTarget> errorPipeTarget = default,
        Option<Map<string, string>> environmentVariables = default
    ) =>
        newCommand(
            Seq([executablePath]) + arguments,
            workingDirPath,
            outPipeTarget,
            errorPipeTarget,
            environmentVariables
        );

    public static Eff<Command> newCommand(
        Seq<string> arguments,
        Option<string> workingDirPath = default,
        Option<PipeTarget> outPipeTarget = default,
        Option<PipeTarget> errorPipeTarget = default,
        Option<Map<string, string>> environmentVariables = default
    ) =>
        lift(() =>
        {
            var headTail = arguments.HeadAndTailSafe();
            if (headTail.IsNone)
                throw Error.New("Cli arguments are empty");

            var (head, tail) = headTail.ValueUnsafe();

            var command = CliWrap
                .Cli.Wrap(head)
                .WithArguments(tail)
                .WithStandardOutputPipe(outPipeTarget.IfNone(defaultOutPipeTarget))
                .WithStandardErrorPipe(errorPipeTarget.IfNone(defaultErrorPipeTarget));

            if (environmentVariables.IsSome)
            {
                var envVars = new ReadOnlyDictionary<string, string>(
                    environmentVariables
                        .ValueUnsafe()
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                );

                command = command.WithEnvironmentVariables(envVars);
            }

            if (workingDirPath.IsSome)
                command = command.WithWorkingDirectory(workingDirPath.ValueUnsafe());

            return command;
        });

    public static Eff<CommandResult> runCommand(Command command) =>
        liftIO(async io => await command.ExecuteAsync(io.Token));

    public static Eff<string> runCommand(string exePath, Seq<string> args) =>
        from builder in Pure(new StringBuilder())
        from command in newCommand(
            exePath,
            args,
            outPipeTarget: PipeTarget.ToStringBuilder(builder)
        )
        from result in runCommand(command)
        from _ in guard(result.IsSuccess, () => Error.New($"Failed to run command {result}"))
        select builder.ToString();
}
