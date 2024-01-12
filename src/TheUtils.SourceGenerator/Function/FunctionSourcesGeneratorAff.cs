namespace TheUtils.SourceGenerator.Function;

using System.Collections.Generic;
using System.Linq;

public static class FunctionSourcesGeneratorAff
{
    public class FuncAff
    {
        public string FuncName { get; set; }
        public string NamespaceName { get; set; }
        public string ParentClassName { get; set; }
        public bool ParentClassIsStatic { get; set; }
        public string ReturnSubTypeName { get; set; }

        public List<InputParameter> Parameters { get; set; } = new();
    }

    public static string GenerateAff(FuncAff meta)
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

        var inputTypes = string.Join(", ", meta
            .Parameters
            .Select(p => p.TypeName));

        var inputAsLambdaParams = string.Join(", ", meta.Parameters
            .Select(p => $"{char.ToLowerInvariant(p.Name[0]) + p.Name.Substring(1)}"));

        return @$"
#pragma warning disable CS0105

using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using LanguageExt.Common;
using static LanguageExt.Prelude;
using TheUtils;
using System.Runtime.CompilerServices;

namespace {meta.NamespaceName}
{{
    using Unit = LanguageExt.Unit;

    {outerClassBegin}
    /// <summary>
    /// Delegate pointing to <see cref=""{meta.FuncName}""/>.
    /// </summary>
    public delegate Aff<{meta.ReturnSubTypeName}> {meta.FuncName}Aff({inputParams});

    /// <summary>
    /// Delegate pointing to <see cref=""{meta.FuncName}""/>.
    /// </summary>
    public delegate ValueTask<Fin<{meta.ReturnSubTypeName}>> {meta.FuncName}Safe({inputParams});

    /// <summary>
    /// Delegate pointing to <see cref=""{meta.FuncName}""/>.
    /// </summary>
    public delegate ValueTask<{meta.ReturnSubTypeName}> {meta.FuncName}Unsafe({inputParams});

    /// <summary>
    /// Delegate pointing to <see cref=""{meta.FuncName}""/>.
    /// </summary>
    public delegate ValueTask<{meta.ReturnSubTypeName}> {meta.FuncName}Func({inputParams});
    {outerClassEnd}
}}

namespace TheUtils.DependencyInjection
{{
    using Unit = LanguageExt.Unit;
    using {meta.NamespaceName};
    using Microsoft.Extensions.DependencyInjection;
    using TheUtils;
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using static TheUtils.FunctionTransforms;

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
                serviceType: typeof({parentClassPrefix}{meta.FuncName}Aff),
                factory: __x__ => new {parentClassPrefix}{meta.FuncName}Aff(
                    ({inputAsLambdaParams}) =>
                        Transform(() =>
                            __x__.GetRequiredService<{parentClassPrefix}{meta.FuncName}>().Invoke({inputAsLambdaParams})
                        )
                ),
                lifetime));

            services.Add(new(
                serviceType: typeof({parentClassPrefix}{meta.FuncName}Safe),
                factory: __x__ => new {parentClassPrefix}{meta.FuncName}Safe(
                    async ({inputAsLambdaParams}) => await 
                        Transform(() =>
                            __x__.GetRequiredService<{parentClassPrefix}{meta.FuncName}>().Invoke({inputAsLambdaParams})
                        ).Run()
                ),
                lifetime));

            services.Add(new(
                serviceType: typeof({parentClassPrefix}{meta.FuncName}Unsafe),
                factory: __x__ => new {parentClassPrefix}{meta.FuncName}Unsafe(
                    async ({inputAsLambdaParams}) => await 
                        Transform(() =>
                            __x__.GetRequiredService<{parentClassPrefix}{meta.FuncName}>().Invoke({inputAsLambdaParams})
                        ).Run().ThrowIfFail()
                ),
                lifetime));

            services.Add(new(
                serviceType: typeof({parentClassPrefix}{meta.FuncName}Func),
                factory: __x__ => new {parentClassPrefix}{meta.FuncName}Func(
                    async ({inputAsLambdaParams}) => await 
                        Transform(() =>
                            __x__.GetRequiredService<{parentClassPrefix}{meta.FuncName}>().Invoke({inputAsLambdaParams})
                        ).Run().ThrowIfFail()
                ),
                lifetime));

            return services;
        }}       
    }}
}}
";
    }
}