namespace TheUtils.SourceGenerator;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public static class SourcesGenerator
{
    public static string GenerateDiExtensions(List<string> functionNames)
    {
        var registrationLines = new StringBuilder();
        var registrations = new StringBuilder();

        foreach (var funcName in functionNames)
        {
            registrations.Append($@"

        public static IServiceCollection Add{funcName}Function
        (
            this IServiceCollection services,
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        )
        {{
            services.Add(new(
                serviceType: typeof({funcName}),
                implementationType: typeof({funcName}),
                lifetime: lifetime));

            services.Add(new(
                serviceType: typeof(I{funcName}),
                factory: x => x.GetService<{funcName}>(),
                lifetime: lifetime));

            services.Add(new (
                serviceType: typeof({funcName}Aff),
                factory: x => x.GetService<I{funcName}>().ToAff(),
                lifetime: lifetime));

            services.Add(new(
                serviceType: typeof({funcName}Safe),
                factory: x => x.GetService<I{funcName}>().ToSafe(),
                lifetime: lifetime));

            services.Add(new(
                serviceType: typeof({funcName}Unsafe),
                factory: x => x.GetService<I{funcName}>().ToUnsafe(),
                lifetime: lifetime));

            return services;
        }}

");
            registrationLines.AppendLine($@"services.Add{funcName}Function(lifetime);");
            registrationLines.Append("\t\t\t");
        }

        return $@"namespace ConsoleApp1
{{
    using Microsoft.Extensions.DependencyInjection;

    public static class ServiceCollectionFunctionExtensions
    {{
        public static IServiceCollection AddAllFunctions
        (
            this IServiceCollection services,
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        )
        {{
            {registrationLines}

            return services;
        }}

        {registrations}
    }}
}}
";
    }

    static string Generate(FuncMetadataWithResult meta)
    {
        return @$"using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using static LanguageExt.Prelude;
using TheUtils;
using System.Runtime.CompilerServices;

namespace {meta.NamespaceName}
{{
    public delegate Aff<{meta.ResultTypeName}> {meta.FuncName}Aff(CancellationToken token);
    public delegate ValueTask<Fin<{meta.ResultTypeName}>> {meta.FuncName}Safe(CancellationToken token);
    public delegate ValueTask<{meta.ResultTypeName}> {meta.FuncName}Unsafe(CancellationToken token);

    public interface I{meta.FuncName} :
        Functions.IFunctionAff<Unit, {meta.ResultTypeName}>,
        Functions.IConvertibleFunction<
            {meta.FuncName}Aff,
            {meta.FuncName}Safe,
            {meta.FuncName}Unsafe> {{}}

    public partial class {meta.FuncName} : I{meta.FuncName}
    {{
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Aff ToAff() => token => Invoke(unit, token);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Safe ToSafe() => token => Invoke(unit, token).Run();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Unsafe ToUnsafe() => token => Invoke(unit, token).Run().ThrowIfFail();
    }}
}}
";
    }

    static string Generate(FuncMetadataWithInputAndResult meta)
    {
        var inputParams = meta.InputType.IsRecord
            ? string.Join(", ", meta
                .Parameters
                .Select(p => $"{p.TypeName} {char.ToLowerInvariant(p.Name[0]) + p.Name.Substring(1)}"))
            : $"{meta.InputTypeName} arg";

        var inputParamsLambda = string.Join(", ", meta
            .Parameters
            .Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name.Substring(1)));

        return @$"using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using static LanguageExt.Prelude;
using TheUtils;
using System.Runtime.CompilerServices;

namespace {meta.NamespaceName}
{{
    public delegate Aff<{meta.ResultTypeName}> {meta.FuncName}Aff(
        {inputParams}, CancellationToken token);

    public delegate ValueTask<Fin<{meta.ResultTypeName}>> {meta.FuncName}Safe(
        {inputParams}, CancellationToken token);

    public delegate ValueTask<{meta.ResultTypeName}> {meta.FuncName}Unsafe(
        {inputParams}, CancellationToken token);

    public interface I{meta.FuncName} :
        Functions.IFunctionAff
        <
            {meta.InputTypeName},
            {meta.ResultTypeName}
        >,
        Functions.IConvertibleFunction<
            {meta.FuncName}Aff,
            {meta.FuncName}Safe,
            {meta.FuncName}Unsafe> {{}}

    public partial class {meta.FuncName} : I{meta.FuncName}
    {{
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Aff ToAff() => ({inputParamsLambda}, token) =>
            Invoke(new({inputParamsLambda}), token);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Safe ToSafe() => ({inputParamsLambda}, token) =>
            Invoke(new({inputParamsLambda}), token).Run();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Unsafe ToUnsafe() => ({inputParamsLambda}, token) =>
             Invoke(new({inputParamsLambda}), token).Run().ThrowIfFail();
    }}
}}
";
    }

    static string Generate(FuncMetadata meta)
    {
        return @$"using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using static LanguageExt.Prelude;
using TheUtils;
using System.Runtime.CompilerServices;

namespace {meta.NamespaceName}
{{
    public delegate Aff<Unit> {meta.FuncName}Aff(CancellationToken token);
    public delegate ValueTask<Fin<Unit>> {meta.FuncName}Safe(CancellationToken token);
    public delegate ValueTask<Unit> {meta.FuncName}Unsafe(CancellationToken token);

    public interface I{meta.FuncName} :
        Functions.IFunctionAff<Unit, Unit>,
        Functions.IConvertibleFunction<
            {meta.FuncName}Aff,
            {meta.FuncName}Safe,
            {meta.FuncName}Unsafe> {{}}

    public partial class {meta.FuncName} : I{meta.FuncName}
    {{
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Aff ToAff() => token => Invoke(unit, token);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Safe ToSafe() => token => Invoke(unit, token).Run();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Unsafe ToUnsafe() => token => Invoke(unit, token).Run().ThrowIfFail();
    }}
}}
";
    }

    public static string GenerateDelegates(FuncMetadata meta) => meta switch
    {
        FuncMetadataWithInputAndResult f => Generate(f),
        FuncMetadataWithResult f => Generate(f),
        { } => Generate(meta),
        _ => throw new ArgumentOutOfRangeException(nameof(meta))
    };
}
