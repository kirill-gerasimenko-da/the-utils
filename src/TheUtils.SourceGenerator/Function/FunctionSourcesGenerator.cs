namespace TheUtils.SourceGenerator.Function;

using System;
using System.Linq;

public static class FunctionSourcesGenerator
{
    static string GenerateAff(FuncMetadata meta)
    {
        var outerClassBegin = meta.ParentClassName != null
            ? $@"public {(meta.ParentClassIsStatic ? "static" : "")} partial class {meta.ParentClassName}
    {{
"
            : "";

        var outerClassEnd = meta.ParentClassName != null ? "}" : "";

        var parentClassPrefix = meta.ParentClassName != null ? $"{meta.ParentClassName}." : "";

        var inputParams = string.Join(", ", meta
            .Parameters
            .Select(p => $"{p.TypeName} {char.ToLowerInvariant(p.Name[0]) + p.Name.Substring(1)}"));

        var inputAsLambdaParams = string.Join(", ", meta.Parameters
            .Select(p => $"{char.ToLowerInvariant(p.Name[0]) + p.Name.Substring(1)}"));

        return @$"using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using LanguageExt.Common;
using static LanguageExt.Prelude;
using TheUtils;
using FluentValidation;
using FluentValidation.Results;
using System.Runtime.CompilerServices;

namespace {meta.NamespaceName}
{{
    {outerClassBegin}

        // generated
        public delegate Aff<{meta.ReturnSubTypeName}> {meta.FuncName}Aff({inputParams});
        public delegate ValueTask<Fin<{meta.ReturnSubTypeName}>> {meta.FuncName}Safe({inputParams});
        public delegate ValueTask<{meta.ReturnSubTypeName}> {meta.FuncName}Unsafe({inputParams});

    {outerClassEnd}
}}

namespace TheUtils.DependencyInjection
{{
    using {meta.NamespaceName};
    using Microsoft.Extensions.DependencyInjection;
    using TheUtils;
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    public static partial class ServiceCollectionFunctionExtensions
    {{
        public static IServiceCollection Add{meta.FuncName}Function
        (
            this IServiceCollection services,
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        )
        {{
            services.Add(new(
                serviceType: typeof({parentClassPrefix}{meta.FuncName}),
                implementationType: typeof({parentClassPrefix}{meta.FuncName}),
                lifetime));

            services.Add(new(
                serviceType: typeof({parentClassPrefix}{meta.FuncName}Aff),
                factory: x => new {parentClassPrefix}{meta.FuncName}Aff(
                    ({inputAsLambdaParams}) =>
                        Eff(() => x.GetRequiredService<{parentClassPrefix}{meta.FuncName}>().Invoke({inputAsLambdaParams})
                            {(meta.ReturnIsValueTask || meta.ReturnIsValueFinTask ? ".ToAff()" : "")}
                            {(meta.ReturnIsValueFinTask ? ".Bind(v => v.ToAff())" : "")})
                            .Bind(identity)
                ),
                lifetime));

            services.Add(new(
                serviceType: typeof({parentClassPrefix}{meta.FuncName}Safe),
                factory: x => new {parentClassPrefix}{meta.FuncName}Safe(
                    async ({inputAsLambdaParams}) =>
                        await x.GetRequiredService<{parentClassPrefix}{meta.FuncName}Aff>()({inputAsLambdaParams}).Run()),
                lifetime));

            services.Add(new(
                serviceType: typeof({parentClassPrefix}{meta.FuncName}Unsafe),
                factory: x => new {parentClassPrefix}{meta.FuncName}Unsafe(
                    async ({inputAsLambdaParams}) =>
                        await x.GetRequiredService<{parentClassPrefix}{meta.FuncName}Aff>()({inputAsLambdaParams}).RunUnsafe()),
                lifetime));

            return services;
        }}       
    }}
}}
";
    }

    static string GenerateEff(FuncMetadata meta)
    {
        var outerClassBegin = meta.ParentClassName != null
            ? $@"public {(meta.ParentClassIsStatic ? "static" : "")} partial class {meta.ParentClassName}
    {{
"
            : "";

        var outerClassEnd = meta.ParentClassName != null ? "}" : "";

        var parentClassPrefix = meta.ParentClassName != null ? $"{meta.ParentClassName}." : "";

        var inputParams = string.Join(", ", meta
            .Parameters
            .Select(p => $"{p.TypeName} {char.ToLowerInvariant(p.Name[0]) + p.Name.Substring(1)}"));

        var inputAsLambdaParams = string.Join(", ", meta.Parameters
            .Select(p => $"{char.ToLowerInvariant(p.Name[0]) + p.Name.Substring(1)}"));

        return @$"using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using LanguageExt.Common;
using static LanguageExt.Prelude;
using TheUtils;
using FluentValidation;
using FluentValidation.Results;
using System.Runtime.CompilerServices;

namespace {meta.NamespaceName}
{{
    {outerClassBegin}

        // generated
        public delegate Eff<{meta.ReturnSubTypeName}> {meta.FuncName}Eff({inputParams});
        public delegate Fin<{meta.ReturnSubTypeName}> {meta.FuncName}Safe({inputParams});
        public delegate {meta.ReturnSubTypeName} {meta.FuncName}Unsafe({inputParams});

    {outerClassEnd}
}}

namespace TheUtils.DependencyInjection
{{
    using {meta.NamespaceName};
    using Microsoft.Extensions.DependencyInjection;
    using TheUtils;
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    public static partial class ServiceCollectionFunctionExtensions
    {{
        public static IServiceCollection Add{meta.FuncName}Function
        (
            this IServiceCollection services,
            ServiceLifetime lifetime
        )
        {{
            services.Add(new(
                serviceType: typeof({parentClassPrefix}{meta.FuncName}),
                implementationType: typeof({parentClassPrefix}{meta.FuncName}),
                lifetime));

            services.Add(new(
                serviceType: typeof({parentClassPrefix}{meta.FuncName}Eff),
                factory: x => new {parentClassPrefix}{meta.FuncName}Eff(
                    ({inputAsLambdaParams}) =>
                        Eff(() => x.GetRequiredService<{parentClassPrefix}{meta.FuncName}>().Invoke({inputAsLambdaParams}))
                            {(meta.ReturnIsEffType ? ".Bind(identity)" : "")}
                            {(meta.ReturnIsEffFin ? ".Bind(v => v.ToEff())" : "")}
                            
                ),
                lifetime));

            services.Add(new(
                serviceType: typeof({parentClassPrefix}{meta.FuncName}Safe),
                factory: x => new {parentClassPrefix}{meta.FuncName}Safe(
                    ({inputAsLambdaParams}) =>
                        x.GetRequiredService<{parentClassPrefix}{meta.FuncName}Eff>()({inputAsLambdaParams}).Run()),
                lifetime));

            services.Add(new(
                serviceType: typeof({parentClassPrefix}{meta.FuncName}Unsafe),
                factory: x => new {parentClassPrefix}{meta.FuncName}Unsafe(
                    ({inputAsLambdaParams}) =>
                        x.GetRequiredService<{parentClassPrefix}{meta.FuncName}Eff>()({inputAsLambdaParams}).RunUnsafe()),
                lifetime));

            return services;
        }}       
    }}
}}
";
    }

    public static string GenerateDelegates(FuncMetadata meta) => meta switch
    {
        { ReturnIsEff: true } => GenerateEff(meta),
        { } => GenerateAff(meta),
        _ => throw new ArgumentOutOfRangeException(nameof(meta))
    };
}