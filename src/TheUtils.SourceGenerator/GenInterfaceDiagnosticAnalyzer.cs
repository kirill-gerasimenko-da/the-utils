namespace TheUtils.SourceGenerator;

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
#pragma warning disable RS1036
public class GenInterfaceDiagnosticAnalyzer : DiagnosticAnalyzer
#pragma warning restore RS1036
{
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.ClassDeclaration);
    }

    private void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        var classDeclarationSyntax = context.Node
            is ClassDeclarationSyntax { AttributeLists.Count: > 0 } node
            ? node
            : null;

        if (classDeclarationSyntax == null)
            return;

        var hasGenAttribute = false;

        foreach (var attributeListSyntax in classDeclarationSyntax.AttributeLists)
        foreach (var attributeSyntax in attributeListSyntax.Attributes)
        {
            if (
                context.SemanticModel.GetSymbolInfo(attributeSyntax).Symbol
                is not IMethodSymbol attributeSymbol
            )
                continue;

            var attributeContainingTypeSymbol = attributeSymbol.ContainingType;
            var fullName = attributeContainingTypeSymbol.ToDisplayString();

            if (fullName == "TheUtils.GenInterfaceAttribute")
            {
                hasGenAttribute = true;
                break;
            }
        }

        if (!hasGenAttribute)
            return;

        var semanticModel = context.SemanticModel;
        if (semanticModel.GetDeclaredSymbol(classDeclarationSyntax) is not { } classSymbol)
            return;

        var isPartial = classDeclarationSyntax.Modifiers.Any(m =>
            m.IsKind(SyntaxKind.PartialKeyword)
        );

        if (!isPartial)
            context.ReportDiagnostic(
                Diagnostic.Create(
                    GenInterfaceGenerator.ClassIsNotPartial,
                    classSymbol.Locations.FirstOrDefault(),
                    classSymbol.Name
                )
            );
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GenInterfaceGenerator.ClassIsNotPartial);
}