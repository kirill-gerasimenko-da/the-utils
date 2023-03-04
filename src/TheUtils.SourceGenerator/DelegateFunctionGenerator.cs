namespace TheUtils.SourceGenerator;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

public record InputParameter
{
    public string Name { get; set; }
    public string TypeName { get; set; }
}

public record FuncMetadata
{
    public string FuncName { get; set; }
    public string NamespaceName { get; set; }
}

public record FuncMetadataWithInputAndResult : FuncMetadataWithResult
{
    public ITypeSymbol InputType { get; set; }
    public string InputTypeName { get; set; }
    public List<InputParameter> Parameters { get; } = new();
}

public record FuncMetadataWithResult : FuncMetadata
{
    public string ResultTypeName { get; set; }
}

[Generator]
[SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1036:Specify analyzer banned API enforcement setting")]
public class DelegateFunctionGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
            .Where(static m => m is not null)!;

        var compilationAndClasses = context.CompilationProvider.Combine(classDeclarations.Collect());

        context.RegisterSourceOutput(compilationAndClasses,
            static (spc, source) => Execute(source.Item1, source.Item2, spc));
    }

    static void Execute(
        Compilation compilation,
        ImmutableArray<ClassDeclarationSyntax> classes,
        SourceProductionContext context)
    {
        if (classes.IsDefaultOrEmpty) return;

        var distinctClasses = classes.Distinct();
        var delegatesToGenerate = GetTypesToGenerate(compilation, distinctClasses, context.CancellationToken);

        foreach (var del in delegatesToGenerate)
        {
            var result = SourcesGenerator.GenerateDelegates(del);
            context.AddSource($"{del.NamespaceName}.{del.FuncName}.g.cs", SourceText.From(result, Encoding.UTF8));
        }
    }

    static List<FuncMetadata> GetTypesToGenerate(
        Compilation compilation,
        IEnumerable<ClassDeclarationSyntax> classes,
        CancellationToken ct)
    {
        var functionsToGenerate = new List<FuncMetadata>();

        var classAttribute = compilation.GetTypeByMetadataName("TheUtils.GenerateDelegatesAttribute");
        if (classAttribute == null)
            return functionsToGenerate;

        foreach (var classDeclarationSyntax in classes)
        {
            ct.ThrowIfCancellationRequested();

            var semanticModel = compilation.GetSemanticModel(classDeclarationSyntax.SyntaxTree);
            if (semanticModel.GetDeclaredSymbol(classDeclarationSyntax) is not INamedTypeSymbol classSymbol)
                continue;

            var baseType = classSymbol.BaseType;

            var funcMetadata = baseType.MetadataName switch
            {
                "FunctionAff`2" => new FuncMetadataWithInputAndResult
                {
                    FuncName = classSymbol.Name,
                    NamespaceName = classSymbol.ContainingNamespace.Name,
                    InputTypeName = baseType.TypeArguments[0].ToMinimalDisplayString(semanticModel, 0),
                    ResultTypeName = baseType.TypeArguments[1].ToMinimalDisplayString(semanticModel, 0),
                    InputType = baseType.TypeArguments[0]
                },
                "FunctionAff`1" => new FuncMetadataWithResult
                {
                    FuncName = classSymbol.Name,
                    NamespaceName = classSymbol.ContainingNamespace.Name,
                    ResultTypeName = baseType.TypeArguments[0].ToMinimalDisplayString(semanticModel, 0)
                },
                "FunctionAff" => new FuncMetadata
                {
                    FuncName = classSymbol.Name,
                    NamespaceName = classSymbol.ContainingNamespace.Name,
                },
                _ => null
            };

            if (funcMetadata is null)
                continue;

            if (funcMetadata is FuncMetadataWithInputAndResult full)
            {
                var inputTypeMembers = full.InputType.GetMembers();

                foreach (var m in inputTypeMembers)
                {
                    if (m is IMethodSymbol ms && ms.MethodKind == MethodKind.Constructor
                                              && ms.DeclaredAccessibility == Accessibility.Public)
                    {
                        foreach (var p in ms.Parameters)
                        {
                            full.Parameters.Add(new InputParameter
                            {
                                Name = p.Name,
                                TypeName = p.Type.ToMinimalDisplayString(semanticModel, 0)
                            });
                        }
                    }
                }
            }

            functionsToGenerate.Add(funcMetadata);
        }

        return functionsToGenerate;
    }

    static bool IsSyntaxTargetForGeneration(SyntaxNode node)
        => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 };

    static ClassDeclarationSyntax? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
        var classDeclarationSyntax = (ClassDeclarationSyntax)context.Node;

        foreach (var attributeListSyntax in classDeclarationSyntax.AttributeLists)
        foreach (var attributeSyntax in attributeListSyntax.Attributes)
        {
            if (context.SemanticModel.GetSymbolInfo(attributeSyntax).Symbol is not IMethodSymbol attributeSymbol)
                continue;

            var attributeContainingTypeSymbol = attributeSymbol.ContainingType;
            var fullName = attributeContainingTypeSymbol.ToDisplayString();

            if (fullName == "TheUtils.GenerateDelegatesAttribute")
                return classDeclarationSyntax;
        }

        return null;
    }
}