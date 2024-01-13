namespace TheUtils.SourceGenerator.GenInterface;

using System.Linq;

public class GenInterfaceSourcesGenerator
{
    public static string GenerateDelegates(ClassMetadata meta)
    {
        var outerClassBegin =
            meta.ParentClassName != null
                ? $@"public {(meta.ParentClassIsStatic ? "static" : "")} partial class {meta.ParentClassName}
    {{
"
                : "";

        var outerClassEnd = meta.ParentClassName != null ? "}" : "";

        var methods = string.Join(
            "\n",
            meta.Methods.Select(meth =>
            {
                var inputParams = string.Join(
                    ", ",
                    meth.Parameters.Select(p => $"{p.TypeName} {p.Name}")
                );

                return @$"
    /// <summary>
    /// <inheritdoc cref=""{meta.ClassName}.{meth.Name}"" />
    /// </summary>
    {meth.ReturnType} {meth.Name}(
        {inputParams}
    );

                ";
            })
        );

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
/// <inheritdoc cref=""{meta.ClassName}""/>
/// </summary>
public interface I{meta.ClassName}
{{
    {methods}
}}

public partial class {meta.ClassName} : I{meta.ClassName} {{ }}

    {outerClassEnd}
}}
";
    }
}