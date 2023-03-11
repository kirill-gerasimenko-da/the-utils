namespace TheUtils.SourceGenerator;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public static class SourcesGenerator
{
    public static string GenerateDiExtensions(List<FuncMetadata> functions)
    {
        var registrations = new StringBuilder();

        foreach (var f in functions.Select(x => (
                     funcName: x.FuncName,
                     funcNamespace: x.NamespaceName,
                     funcParentType: x.ParentClassName,
                     isEff: x.IsEff)))
        {
            var funcPrefix = f.funcParentType != null ? $"{f.funcParentType}." : "";

            var funcFullName = $"{f.funcNamespace}.{funcPrefix}{f.funcName}";
            var funcFullNameEffect = $"{funcFullName}{(f.isEff ? "Eff" : "Aff")}";
            var funcFullNameUnsafe = $"{funcFullName}Unsafe";
            var funcFullNameSafe = $"{funcFullName}Safe";
            var funcInterfaceFullName = $"{f.funcNamespace}.{funcPrefix}I{f.funcName}";
            var funcConvertibleFullName =
                $"IConvertibleFunction<{funcFullNameEffect}, {funcFullNameSafe}, {funcFullNameUnsafe}>";

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
                serviceType: typeof({funcFullNameEffect}),
                factory: x => (({funcConvertibleFullName}) x.GetService<{funcInterfaceFullName}>()).ToEffect(),
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
        }

        return $@"namespace ConsoleApp1
{{
    using Microsoft.Extensions.DependencyInjection;
    using TheUtils;
    using static TheUtils.Functions;

    public static partial class ServiceCollectionFunctionExtensions
    {{
        {registrations}
    }}
}}
";
    }

    static string Generate(FuncMetadataWithResult meta)
    {
        var outerClassBegin = meta.ParentClassName != null
            ? $@"public {(meta.ParentClassIsStatic ? "static" : "")} partial class {meta.ParentClassName}
    {{
"
            : "";

        var outerClassEnd = meta.ParentClassName != null ? "}" : "";

        var effectSuffix = meta.IsEff ? "Eff" : "Aff";

        return @$"using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using static LanguageExt.Prelude;
using TheUtils;
using System.Runtime.CompilerServices;

namespace {meta.NamespaceName}
{{
    {outerClassBegin}
    public delegate {effectSuffix}<{meta.ResultTypeName}> {meta.FuncName}{effectSuffix}(
        {(meta.IsEff ? "" : "CancellationToken token")});

    public delegate ValueTask<Fin<{meta.ResultTypeName}>> {meta.FuncName}Safe(
        {(meta.IsEff ? "" : "CancellationToken token")});

    public delegate ValueTask<{meta.ResultTypeName}> {meta.FuncName}Unsafe(
        {(meta.IsEff ? "" : "CancellationToken token")});

    public interface I{meta.FuncName} : Functions.IFunction{effectSuffix}
    <
        Unit,
        {meta.ResultTypeName}
    > {{}}

    public partial class {meta.FuncName} : 
        I{meta.FuncName},
        Functions.IConvertibleFunction<
            {meta.FuncName}{effectSuffix},
            {meta.FuncName}Safe,
            {meta.FuncName}Unsafe>
    {{
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}{effectSuffix} ToEffect() =>
            {(meta.IsEff ? "() => Invoke(unit)" : "token => Invoke(unit, token)")};

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Safe ToSafe() => 
            {(meta.IsEff ? "() => Invoke(unit)" : "token => Invoke(unit, token)")}.Run();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Unsafe ToUnsafe() => 
            {(meta.IsEff ? "() => Invoke(unit)" : "token => Invoke(unit, token)")}.Run().ThrowIfFail();
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

        var outerClassBegin = meta.ParentClassName != null
            ? $@"public {(meta.ParentClassIsStatic ? "static" : "")} partial class {meta.ParentClassName}
    {{
"
            : "";

        var outerClassEnd = meta.ParentClassName != null ? "}" : "";

        var effectSuffix = meta.IsEff ? "Eff" : "Aff";

        return @$"using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using static LanguageExt.Prelude;
using TheUtils;
using System.Runtime.CompilerServices;

namespace {meta.NamespaceName}
{{
    {outerClassBegin}
    public delegate {effectSuffix}<{meta.ResultTypeName}> {meta.FuncName}{effectSuffix}(
        {inputParams}
        {(meta.IsEff ? "" : ", CancellationToken token")});

    public delegate ValueTask<Fin<{meta.ResultTypeName}>> {meta.FuncName}Safe(
        {inputParams}
        {(meta.IsEff ? "" : ", CancellationToken token")});

    public delegate ValueTask<{meta.ResultTypeName}> {meta.FuncName}Unsafe(
        {inputParams}
        {(meta.IsEff ? "" : ", CancellationToken token")});

    public interface I{meta.FuncName} : Functions.IFunction{effectSuffix}
        <
            {meta.InputTypeName},
            {meta.ResultTypeName}
        > {{}}

    public partial class {meta.FuncName} : 
        I{meta.FuncName},
        Functions.IConvertibleFunction<
            {meta.FuncName}{effectSuffix},
            {meta.FuncName}Safe,
            {meta.FuncName}Unsafe>
    {{
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}{effectSuffix} ToEffect() => 
            ({inputParamsLambda}{(meta.IsEff ? "": ", token")}) =>
            Invoke(new({inputParamsLambda}){(meta.IsEff ? "": ", token")});

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Safe ToSafe() => 
            ({inputParamsLambda}{(meta.IsEff ? "": ", token")}) =>
            Invoke(new({inputParamsLambda}){(meta.IsEff ? "": ", token")}).Run();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Unsafe ToUnsafe() => 
            ({inputParamsLambda}{(meta.IsEff ? "": ", token")}) =>
            Invoke(new({inputParamsLambda}){(meta.IsEff ? "": ", token")}).Run().ThrowIfFail();
    }}
    {outerClassEnd}
}}
";
    }

    static string Generate(FuncMetadata meta)
    {
        var outerClassBegin = meta.ParentClassName != null
            ? $@"public {(meta.ParentClassIsStatic ? "static" : "")} partial class {meta.ParentClassName}
    {{
"
            : "";

        var outerClassEnd = meta.ParentClassName != null ? "}" : "";
        
        var effectSuffix = meta.IsEff ? "Eff" : "Aff";

        return @$"using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using static LanguageExt.Prelude;
using TheUtils;
using System.Runtime.CompilerServices;

namespace {meta.NamespaceName}
{{
    {outerClassBegin}
    public delegate {effectSuffix}<Unit> {meta.FuncName}{effectSuffix}(
        {(meta.IsEff ? "" : "CancellationToken token")});

    public delegate ValueTask<Fin<Unit>> {meta.FuncName}Safe(
        {(meta.IsEff ? "" : "CancellationToken token")});

    public delegate ValueTask<Unit> {meta.FuncName}Unsafe(
        {(meta.IsEff ? "" : "CancellationToken token")});

    public interface I{meta.FuncName} : Functions.IFunction{effectSuffix}<Unit, Unit> {{}}

    public partial class {meta.FuncName} : 
        I{meta.FuncName},
        Functions.IConvertibleFunction<
            {meta.FuncName}{effectSuffix},
            {meta.FuncName}Safe,
            {meta.FuncName}Unsafe>
    {{
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}{effectSuffix} ToEffect() =>
            {(meta.IsEff ? "() => Invoke(unit)" : "token => Invoke(unit, token)")};

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Safe ToSafe() => 
            {(meta.IsEff ? "() => Invoke(unit)" : "token => Invoke(unit, token)")}.Run();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public {meta.FuncName}Unsafe ToUnsafe() => 
            {(meta.IsEff ? "() => Invoke(unit)" : "token => Invoke(unit, token)")}.Run().ThrowIfFail();
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