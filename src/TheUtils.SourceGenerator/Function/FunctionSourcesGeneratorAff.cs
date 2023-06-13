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

        return @$"using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using LanguageExt.Common;
using static LanguageExt.Prelude;
using TheUtils;
using System.Runtime.CompilerServices;

namespace {meta.NamespaceName}
{{
    {outerClassBegin}
    public delegate Aff<{meta.ReturnSubTypeName}> {meta.FuncName}Aff({inputParams});
    public delegate ValueTask<Fin<{meta.ReturnSubTypeName}>> {meta.FuncName}Safe({inputParams});
    public delegate ValueTask<{meta.ReturnSubTypeName}> {meta.FuncName}Unsafe({inputParams});
    {outerClassEnd}

public static partial class {meta.FuncName}DelegateConverters
{{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Func<{inputTypes}, ValueTask<{meta.ReturnSubTypeName}>> ToFun(this {parentClassPrefix}{meta.FuncName}Unsafe del) =>
        ({inputAsLambdaParams}) => del({inputAsLambdaParams});
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Func<{inputTypes}, ValueTask<Fin<{meta.ReturnSubTypeName}>>> ToFun(this {parentClassPrefix}{meta.FuncName}Safe del) =>
        ({inputAsLambdaParams}) => del({inputAsLambdaParams});
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Func<{inputTypes}, Aff<{meta.ReturnSubTypeName}>> ToFun(this {parentClassPrefix}{meta.FuncName}Aff del) =>
        ({inputAsLambdaParams}) => del({inputAsLambdaParams});
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static {parentClassPrefix}{meta.FuncName}Unsafe ToDel(this Func<{inputTypes}, ValueTask<{meta.ReturnSubTypeName}>> fun) =>
        ({inputAsLambdaParams}) => fun({inputAsLambdaParams});
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static {parentClassPrefix}{meta.FuncName}Safe ToDel(this Func<{inputTypes}, ValueTask<Fin<{meta.ReturnSubTypeName}>>> fun) =>
        ({inputAsLambdaParams}) => fun({inputAsLambdaParams});
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static {parentClassPrefix}{meta.FuncName}Aff ToDel(this Func<{inputTypes}, Aff<{meta.ReturnSubTypeName}>> fun) =>
        ({inputAsLambdaParams}) => fun({inputAsLambdaParams});
}}

}}

namespace TheUtils.DependencyInjection
{{
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
                factory: x => new {parentClassPrefix}{meta.FuncName}Aff(
                    ({inputAsLambdaParams}) =>
                        Transform(() =>
                            x.GetRequiredService<{parentClassPrefix}{meta.FuncName}>().Invoke({inputAsLambdaParams})
                        )
                ),
                lifetime));

            services.Add(new(
                serviceType: typeof({parentClassPrefix}{meta.FuncName}Safe),
                factory: x => new {parentClassPrefix}{meta.FuncName}Safe(
                    async ({inputAsLambdaParams}) => await 
                        Transform(() =>
                            x.GetRequiredService<{parentClassPrefix}{meta.FuncName}>().Invoke({inputAsLambdaParams})
                        ).Run()
                ),
                lifetime));

            services.Add(new(
                serviceType: typeof({parentClassPrefix}{meta.FuncName}Unsafe),
                factory: x => new {parentClassPrefix}{meta.FuncName}Unsafe(
                    async ({inputAsLambdaParams}) => await 
                        Transform(() =>
                            x.GetRequiredService<{parentClassPrefix}{meta.FuncName}>().Invoke({inputAsLambdaParams})
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