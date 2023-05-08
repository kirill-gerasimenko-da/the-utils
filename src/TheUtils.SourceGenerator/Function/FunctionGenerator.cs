namespace TheUtils.SourceGenerator.Function;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    public ClassDeclarationSyntax ClassDeclarationSyntax { get; set; }

    public string FuncName { get; set; }
    public string NamespaceName { get; set; }
    public string ParentClassName { get; set; }
    public bool ParentClassIsStatic { get; set; }

    public ITypeSymbol Type { get; set; }
    public string TypeName { get; set; }
    public List<InputParameter> Parameters { get; } = new();

    public bool ReturnIsValueFinTask { get; set; }
    public bool ReturnIsValueTask { get; set; }
    public bool ReturnIsAff { get; set; }

    public bool ReturnIsEffFin { get; set; }
    public bool ReturnIsEffRegularType { get; set; }
    public bool ReturnIsEffType { get; set; }
    public bool ReturnIsEff { get; set; }

    public string ReturnTypeName { get; set; }
    public string ReturnSubTypeName { get; set; }

    public bool FoundInvokeFunction { get; set; }
}

[Generator]
[SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1036:Specify analyzer banned API enforcement setting")]
public class FunctionGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor NoInvokeMethodFound = new(
        id: "TUTLS01",
        title: "Couldn't find 'Invoke' method",
        messageFormat: "Could not find required 'Invoke' method on '{0}'",
        category: "FunctionGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);


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
            if (!del.FoundInvokeFunction)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(NoInvokeMethodFound, del.ClassDeclarationSyntax.GetLocation(), del.FuncName));
            }
            else
            {
                var result = FunctionSourcesGenerator.GenerateDelegates(del);
                context.AddSource($"{del.NamespaceName}.{del.FuncName}.g.cs", SourceText.From(result, Encoding.UTF8));
            }
        }
    }

    static List<FuncMetadata> GetTypesToGenerate(
        Compilation compilation,
        IEnumerable<ClassDeclarationSyntax> classes,
        CancellationToken ct)
    {
        var functionsToGenerate = new List<FuncMetadata>();

        var recordAttribute = compilation.GetTypeByMetadataName("TheUtils.FunctionAttribute");
        if (recordAttribute == null)
            return functionsToGenerate;

        foreach (var classDeclarationSyntax in classes)
        {
            ct.ThrowIfCancellationRequested();

            var semanticModel = compilation.GetSemanticModel(classDeclarationSyntax.SyntaxTree);
            if (semanticModel.GetDeclaredSymbol(classDeclarationSyntax) is not INamedTypeSymbol classSymbol)
                continue;

            var func = new FuncMetadata
            {
                ClassDeclarationSyntax = classDeclarationSyntax,
                Type = classSymbol,
                TypeName = classSymbol.ToMinimalDisplayString(semanticModel, 0),
                FuncName = classSymbol.Name,
                NamespaceName = classSymbol.ContainingNamespace.ToMinimalDisplayString(semanticModel, 0),
            };

            // find parameters
            var members = classSymbol.GetMembers();
            foreach (var m in members)
            {
                if (m is IMethodSymbol msr && msr.MethodKind == MethodKind.Ordinary
                                           && msr.IsStatic == false
                                           && msr.DeclaredAccessibility == Accessibility.Public)
                {
                    if (msr.Name == "Invoke" && msr.ReturnType.MetadataName == "Aff`1")
                    {
                        func.ReturnIsAff = true;
                        func.ReturnTypeName = msr.ReturnType.ToMinimalDisplayString(semanticModel, 0);
                        func.ReturnSubTypeName = Regex.Match(func.ReturnTypeName, @"^LanguageExt\.Aff\<(.*)\>$")
                            .Groups[1].Value;

                        foreach (var p in msr.Parameters)
                        {
                            func.Parameters.Add(new InputParameter
                            {
                                Name = p.Name,
                                TypeName = p.Type.ToMinimalDisplayString(semanticModel, 0),
                            });
                        }

                        func.FoundInvokeFunction = true;
                    }
                    else if (msr.Name == "Invoke" && msr.ReturnType.MetadataName == "Eff`1")
                    {
                        func.ReturnIsEff = true;
                        func.ReturnIsEffType = true;
                        func.ReturnTypeName = msr.ReturnType.ToMinimalDisplayString(semanticModel, 0);
                        func.ReturnSubTypeName = Regex.Match(func.ReturnTypeName, @"^LanguageExt\.Eff\<(.*)\>$")
                            .Groups[1].Value;

                        foreach (var p in msr.Parameters)
                        {
                            func.Parameters.Add(new InputParameter
                            {
                                Name = p.Name,
                                TypeName = p.Type.ToMinimalDisplayString(semanticModel, 0),
                            });
                        }

                        func.FoundInvokeFunction = true;
                    }
                    else if (msr.Name == "Invoke" && msr.ReturnType.MetadataName == "Fin`1")
                    {
                        func.ReturnIsEff = true;
                        func.ReturnIsEffFin = true;

                        func.ReturnTypeName = msr.ReturnType.ToMinimalDisplayString(semanticModel, 0);
                        func.ReturnSubTypeName =
                            Regex.Match(func.ReturnTypeName, @"^LanguageExt\.Fin\<(.*)\>$").Groups[1].Value;

                        foreach (var p in msr.Parameters)
                        {
                            func.Parameters.Add(new InputParameter
                            {
                                Name = p.Name,
                                TypeName = p.Type.ToMinimalDisplayString(semanticModel, 0),
                            });
                        }

                        func.FoundInvokeFunction = true;
                    }
                    else if (msr.Name == "Invoke" && msr.ReturnType.MetadataName == "ValueTask`1")
                    {
                        func.ReturnTypeName = msr.ReturnType.ToMinimalDisplayString(semanticModel, 0);

                        func.ReturnIsValueFinTask =
                            Regex.IsMatch(func.ReturnTypeName, @"^ValueTask\<LanguageExt\.Fin\<.*\>\>$");

                        if (func.ReturnIsValueFinTask)
                            func.ReturnSubTypeName =
                                Regex.Match(func.ReturnTypeName, @"^ValueTask\<LanguageExt\.Fin\<(.*)\>\>$").Groups[1]
                                    .Value;
                        else
                        {
                            func.ReturnSubTypeName =
                                Regex.Match(func.ReturnTypeName, @"^ValueTask\<(.*)\>$").Groups[1].Value;
                            func.ReturnIsValueTask = true;
                        }

                        foreach (var p in msr.Parameters)
                        {
                            func.Parameters.Add(new InputParameter
                            {
                                Name = p.Name,
                                TypeName = p.Type.ToMinimalDisplayString(semanticModel, 0),
                            });
                        }

                        func.FoundInvokeFunction = true;
                    }
                    else if (msr.Name == "Invoke")
                    {
                        func.ReturnIsEff = true;
                        func.ReturnIsEffRegularType = true;
                        func.ReturnTypeName = msr.ReturnType.ToMinimalDisplayString(semanticModel, 0);
                        func.ReturnSubTypeName = func.ReturnTypeName; 
                        
                        foreach (var p in msr.Parameters)
                        {
                            func.Parameters.Add(new InputParameter
                            {
                                Name = p.Name,
                                TypeName = p.Type.ToMinimalDisplayString(semanticModel, 0),
                            });
                        }
                        
                        func.FoundInvokeFunction = true;
                    }
                }
            }

            if (classSymbol.ContainingType is { DeclaredAccessibility: Accessibility.Public })
            {
                func.ParentClassName = classSymbol.ContainingType.Name;
                func.ParentClassIsStatic = classSymbol.ContainingType.IsStatic;
            }

            functionsToGenerate.Add(func);
        }

        return functionsToGenerate;
    }

    static bool IsSyntaxTargetForGeneration(SyntaxNode node)
        => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 };

    static ClassDeclarationSyntax GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
        var classDeclarationSyntax = (ClassDeclarationSyntax)context.Node;

        foreach (var attributeListSyntax in classDeclarationSyntax.AttributeLists)
        foreach (var attributeSyntax in attributeListSyntax.Attributes)
        {
            if (context.SemanticModel.GetSymbolInfo(attributeSyntax).Symbol is not IMethodSymbol attributeSymbol)
                continue;

            var attributeContainingTypeSymbol = attributeSymbol.ContainingType;
            var fullName = attributeContainingTypeSymbol.ToDisplayString();

            if (fullName == "TheUtils.FunctionAttribute")
                return classDeclarationSyntax;
        }

        return null;
    }
}