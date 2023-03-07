namespace TheUtils.SourceGenerator;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public static class SourcesGenerator
{
    public static string GenerateDiExtensions(List<FuncMetadata> functions)
    {
        var registrationLines = new StringBuilder();
        var registrations = new StringBuilder();

        foreach (var f in functions.Select(x => (
                     funcName: x.FuncName,
                     funcNamespace: x.NamespaceName,
                     funcParentType: x.ParentClassName)))
        {
            var funcPrefix = f.funcParentType != null ? $"{f.funcParentType}." : "";
            
            var funcFullName = $"{funcPrefix}{f.funcNamespace}.{f.funcName}";
            var funcFullNameAff = $"{funcFullName}Aff";
            var funcFullNameUnsafe = $"{funcFullName}Unsafe";
            var funcFullNameSafe = $"{funcFullName}Safe";
            var funcInterfaceFullName = $"{funcPrefix}{f.funcNamespace}.I{f.funcName}";
            var funcConvertibleFullName = $"IConvertibleFunction<{funcFullNameAff}, {funcFullNameSafe}, {funcFullNameUnsafe}>";
            
            registrations.Append($@"
        public static IServiceCollection Add{f.funcName}Function
        (
            this IServiceCollection services,
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        )
        {{
            services.Add(new(
                serviceType: typeof({funcInterfaceFullName}),
                implementationType: typeof({funcFullName}),
                lifetime: lifetime));

            services.Add(new (
                serviceType: typeof({funcFullNameAff}),
                factory: x => (({funcConvertibleFullName}) x.GetService<{funcInterfaceFullName}>()).ToAff(),
                lifetime: lifetime));

            services.Add(new(
                serviceType: typeof({funcFullNameSafe}),
                factory: x => (({funcConvertibleFullName}) x.GetService<{funcInterfaceFullName}>()).ToSafe(),
                lifetime: lifetime));

            services.Add(new(
                serviceType: typeof({funcFullNameUnsafe}),
                factory: x => (({funcConvertibleFullName}) x.GetService<{funcInterfaceFullName}>()).ToUnsafe(),
                lifetime: lifetime));

            return services;
        }}
");
            registrationLines.AppendLine($@"services.Add{f.funcName}Function(lifetime);");
            registrationLines.Append("\t\t\t");
        }

        return $@"namespace ConsoleApp1
{{
    using Microsoft.Extensions.DependencyInjection;
    using TheUtils;
    using static TheUtils.Functions;

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
        
        var outerClassBegin = meta.ParentClassName != null ? $@"public {(meta.ParentClassIsStatic ? "static" : "")} partial class {meta.ParentClassName}
    {{
" : "";
        
        var outerClassEnd = meta.ParentClassName != null ? "}}" : "";
        
        return @$"using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using static LanguageExt.Prelude;
using TheUtils;
using System.Runtime.CompilerServices;

namespace {meta.NamespaceName}
{{
    {outerClassBegin}
    public delegate Aff<{meta.ResultTypeName}> {meta.FuncName}Aff(CancellationToken token);
    public delegate ValueTask<Fin<{meta.ResultTypeName}>> {meta.FuncName}Safe(CancellationToken token);
    public delegate ValueTask<{meta.ResultTypeName}> {meta.FuncName}Unsafe(CancellationToken token);

    public interface I{meta.FuncName} : Functions.IFunctionAff
    <
        Unit,
        {meta.ResultTypeName}
    > {{}}

    public partial class {meta.FuncName} : 
        I{meta.FuncName},
        Functions.IConvertibleFunction<
            {meta.FuncName}Aff,
            {meta.FuncName}Safe,
            {meta.FuncName}Unsafe>
    {{
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Aff ToAff() => token => Invoke(unit, token);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Safe ToSafe() => token => Invoke(unit, token).Run();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Unsafe ToUnsafe() => token => Invoke(unit, token).Run().ThrowIfFail();
    }}
    {outerClassEnd}
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
        
        var outerClassBegin = meta.ParentClassName != null ? $@"public {(meta.ParentClassIsStatic ? "static" : "")} partial class {meta.ParentClassName}
    {{
" : "";
        
        var outerClassEnd = meta.ParentClassName != null ? "}}" : "";

        return @$"using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using static LanguageExt.Prelude;
using TheUtils;
using System.Runtime.CompilerServices;

namespace {meta.NamespaceName}
{{
    {outerClassBegin}
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
        > {{}}

    public partial class {meta.FuncName} : 
        I{meta.FuncName},
        Functions.IConvertibleFunction<
            {meta.FuncName}Aff,
            {meta.FuncName}Safe,
            {meta.FuncName}Unsafe>
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
    {outerClassEnd}
}}
";
    }

    static string Generate(FuncMetadata meta)
    {
        var outerClassBegin = meta.ParentClassName != null ? $@"public {(meta.ParentClassIsStatic ? "static" : "")} partial class {meta.ParentClassName}
    {{
" : "";
        
        var outerClassEnd = meta.ParentClassName != null ? "}}" : "";

        return @$"using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using static LanguageExt.Prelude;
using TheUtils;
using System.Runtime.CompilerServices;

namespace {meta.NamespaceName}
{{
    {outerClassBegin}
    public delegate Aff<Unit> {meta.FuncName}Aff(CancellationToken token);
    public delegate ValueTask<Fin<Unit>> {meta.FuncName}Safe(CancellationToken token);
    public delegate ValueTask<Unit> {meta.FuncName}Unsafe(CancellationToken token);

    public interface I{meta.FuncName} : Functions.IFunctionAff<Unit, Unit> {{}}

    public partial class {meta.FuncName} : 
        I{meta.FuncName},
        Functions.IConvertibleFunction<
            {meta.FuncName}Aff,
            {meta.FuncName}Safe,
            {meta.FuncName}Unsafe>
    {{
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Aff ToAff() => token => Invoke(unit, token);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Safe ToSafe() => token => Invoke(unit, token).Run();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Unsafe ToUnsafe() => token => Invoke(unit, token).Run().ThrowIfFail();
    }}
    {outerClassEnd}
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
