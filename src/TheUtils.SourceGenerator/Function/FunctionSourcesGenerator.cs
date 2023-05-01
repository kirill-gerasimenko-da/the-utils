namespace TheUtils.SourceGenerator.Function;

using System;
using System.Linq;

public static class FunctionSourcesGenerator
{
    static string Generate(FuncMetadata meta)
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
    using static TheUtils.Functions;
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
                            {(meta.ReturnIsValueFinTask ? ".Bind(v => v.ToAff())": "")})
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

    public static string GenerateDelegates(FuncMetadata meta) => meta switch
    {
        { } => Generate(meta),
        _ => throw new ArgumentOutOfRangeException(nameof(meta))
    };
}