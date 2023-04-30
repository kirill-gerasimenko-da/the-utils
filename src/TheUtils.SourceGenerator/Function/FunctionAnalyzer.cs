namespace TheUtils.SourceGenerator.Function;

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class FunctionAnalyzer : DiagnosticAnalyzer
{
    static readonly DiagnosticDescriptor NoInvokeMethodFound = new(
        id: "THEUTILS01",
        title: "Couldn't find 'Invoke' method",
        messageFormat: "Could not find required 'Invoke' method on '{0}'",
        category: "FunctionGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(NoInvokeMethodFound);

    public override void Initialize(AnalysisContext context)
    {
        // context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze |
        //                                        GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        var classDeclarationSyntax = context.Node as ClassDeclarationSyntax;
        if (classDeclarationSyntax == null)
            return;

        var funcs = FunctionGenerator.GetTypesToGenerate(
            context.Compilation,
            new[] { classDeclarationSyntax },
            context.CancellationToken);

        foreach (var func in funcs.Where(func => !func.FoundInvokeFunction))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(NoInvokeMethodFound, classDeclarationSyntax.GetLocation(),
                    func.FuncName));
        }
    }
}