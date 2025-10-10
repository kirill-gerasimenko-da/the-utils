namespace TheUtils.SourceGenerator.GenInterface;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

public record Method
{
    public string Name { get; set; }
    public string ReturnType { get; set; }
    public List<InputParameter> Parameters { get; set; } = new();
    public string TypeParameters { get; set; } = "";
    public string TypeConstraints { get; set; } = "";
}

public record InputParameter
{
    public string Name { get; set; }
    public string TypeName { get; set; }
    public bool IsDefault { get; set; }
    public string Default { get; set; }
}

public record ClassMetadata
{
    public ClassDeclarationSyntax ClassDeclarationSyntax { get; set; }

    public string ClassName { get; set; }
    public string NamespaceName { get; set; }

    public string ParentClassName { get; set; }
    public bool ParentClassIsStatic { get; set; }

    public List<Method> Methods { get; set; } = new();

    public string TypeParameters { get; set; } = "";
    public string TypeConstraints { get; set; } = "";
}

[Generator]
[SuppressMessage(
    "MicrosoftCodeAnalysisCorrectness",
    "RS1036:Specify analyzer banned API enforcement setting"
)]
public class GenInterfaceGenerator : IIncrementalGenerator
{
    public static readonly DiagnosticDescriptor ClassIsNotPartial = new(
        id: "TUTLS02",
        title: "The class MUST be partial",
        messageFormat: "The class '{0}' MUST be partial",
        category: "GenInterfaceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDeclarations = context
            .SyntaxProvider.CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx)
            )
            .Where(static m => m is not null)!;

        var compilationAndClasses = context.CompilationProvider.Combine(
            classDeclarations.Collect()
        );

        context.RegisterSourceOutput(
            compilationAndClasses,
            static (spc, source) => Execute(source.Item1, source.Item2, spc)
        );
    }

    static void Execute(
        Compilation compilation,
        ImmutableArray<ClassDeclarationSyntax> classes,
        SourceProductionContext context
    )
    {
        if (classes.IsDefaultOrEmpty)
            return;

        var distinctClasses = classes.Distinct();
        var classesToGenerate = GetTypesForGeneration(
            compilation,
            distinctClasses,
            context.CancellationToken
        );

        foreach (var cls in classesToGenerate)
        {
            var result = GenInterfaceSourcesGenerator.GenerateDelegates(cls);

            context.AddSource(
                $"{cls.NamespaceName}.{cls.ClassName}.Interface.g.cs",
                SourceText.From(result, Encoding.UTF8)
            );
        }
    }

    static List<ClassMetadata> GetTypesForGeneration(
        Compilation compilation,
        IEnumerable<ClassDeclarationSyntax> classes,
        CancellationToken ct
    )
    {
        var classesForGeneration = new List<ClassMetadata>();

        if (
            compilation.GetTypeByMetadataName("TheUtils.GenInterfaceAttribute") == null
            && compilation.GetTypeByMetadataName("TheUtils.GenerateInterfaceAttribute") == null
        )
            return classesForGeneration;

        foreach (var classDeclarationSyntax in classes)
        {
            ct.ThrowIfCancellationRequested();

            var semanticModel = compilation.GetSemanticModel(classDeclarationSyntax.SyntaxTree);
            if (
                semanticModel.GetDeclaredSymbol(classDeclarationSyntax)
                is not INamedTypeSymbol classSymbol
            )
                continue;

            var class_ = new ClassMetadata
            {
                ClassDeclarationSyntax = classDeclarationSyntax,
                ClassName = classSymbol.Name,
                NamespaceName = classSymbol.ContainingNamespace.ToMinimalDisplayString(
                    semanticModel,
                    0
                ),
                TypeParameters = GetTypeParameters(classSymbol),
                TypeConstraints = GetTypeConstraints(classSymbol),
            };

            // find parameters
            var members = classSymbol.GetMembers();
            foreach (var m in members)
            {
                if (
                    m is IMethodSymbol msr
                    && msr.MethodKind == MethodKind.Ordinary
                    && msr.IsStatic == false
                    && msr.DeclaredAccessibility == Accessibility.Public
                )
                {
                    var meth = new Method
                    {
                        Name = msr.Name,
                        ReturnType = GetFullyQualifiedTypeName(msr.ReturnType),
                        TypeParameters = GetMethodTypeParameters(msr),
                        TypeConstraints = GetMethodTypeConstraints(msr),
                    };

                    foreach (var p in msr.Parameters)
                    {
                        var def = GetDefaultValue(p);

                        meth.Parameters.Add(
                            new InputParameter
                            {
                                Name = p.Name,
                                TypeName = GetFullyQualifiedTypeName(p.Type),
                                Default = def,
                                IsDefault = def != null,
                            }
                        );
                    }

                    class_.Methods.Add(meth);
                }
            }

            if (classSymbol.ContainingType is { DeclaredAccessibility: Accessibility.Public })
            {
                class_.ParentClassName = classSymbol.ContainingType.Name;
                class_.ParentClassIsStatic = classSymbol.ContainingType.IsStatic;
            }

            classesForGeneration.Add(class_);
        }

        return classesForGeneration;
    }

    static string GetDefaultValue(IParameterSymbol p)
    {
        string def = null;

        if (p.DeclaringSyntaxReferences[0].GetSyntax() is ParameterSyntax { Default: not null } syn)
            def = syn.Default.ToFullString();

        return def;
    }

    static string GetFullyQualifiedTypeName(ITypeSymbol type)
    {
        return type.ToDisplayString(
            new SymbolDisplayFormat(
                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            )
        );
    }

    static string GetTypeParameters(INamedTypeSymbol typeSymbol)
    {
        if (!typeSymbol.TypeParameters.Any())
            return "";

        var parameters = string.Join(", ", typeSymbol.TypeParameters.Select(tp => tp.Name));
        return $"<{parameters}>";
    }

    static string GetTypeConstraints(INamedTypeSymbol typeSymbol)
    {
        if (!typeSymbol.TypeParameters.Any())
            return "";

        var constraints = new List<string>();
        foreach (var typeParam in typeSymbol.TypeParameters)
        {
            var constraintsList = new List<string>();

            if (typeParam.HasReferenceTypeConstraint)
                constraintsList.Add("class");
            if (typeParam.HasValueTypeConstraint)
                constraintsList.Add("struct");
            if (typeParam.HasUnmanagedTypeConstraint)
                constraintsList.Add("unmanaged");
            if (typeParam.HasNotNullConstraint)
                constraintsList.Add("notnull");

            foreach (var constraintType in typeParam.ConstraintTypes)
            {
                constraintsList.Add(GetFullyQualifiedTypeName(constraintType));
            }

            if (typeParam.HasConstructorConstraint)
                constraintsList.Add("new()");

            if (constraintsList.Count > 0)
            {
                constraints.Add($"where {typeParam.Name} : {string.Join(", ", constraintsList)}");
            }
        }

        return constraints.Count > 0 ? "\n        " + string.Join("\n        ", constraints) : "";
    }

    static string GetMethodTypeParameters(IMethodSymbol methodSymbol)
    {
        if (!methodSymbol.TypeParameters.Any())
            return "";

        var parameters = string.Join(", ", methodSymbol.TypeParameters.Select(tp => tp.Name));
        return $"<{parameters}>";
    }

    static string GetMethodTypeConstraints(IMethodSymbol methodSymbol)
    {
        if (!methodSymbol.TypeParameters.Any())
            return "";

        var constraints = new List<string>();
        foreach (var typeParam in methodSymbol.TypeParameters)
        {
            var constraintsList = new List<string>();

            if (typeParam.HasReferenceTypeConstraint)
                constraintsList.Add("class");
            if (typeParam.HasValueTypeConstraint)
                constraintsList.Add("struct");
            if (typeParam.HasUnmanagedTypeConstraint)
                constraintsList.Add("unmanaged");
            if (typeParam.HasNotNullConstraint)
                constraintsList.Add("notnull");

            foreach (var constraintType in typeParam.ConstraintTypes)
            {
                constraintsList.Add(GetFullyQualifiedTypeName(constraintType));
            }

            if (typeParam.HasConstructorConstraint)
                constraintsList.Add("new()");

            if (constraintsList.Count > 0)
            {
                constraints.Add($"where {typeParam.Name} : {string.Join(", ", constraintsList)}");
            }
        }

        return constraints.Count > 0
            ? "\n            " + string.Join("\n            ", constraints)
            : "";
    }

    static bool IsSyntaxTargetForGeneration(SyntaxNode node) =>
        node is ClassDeclarationSyntax { AttributeLists.Count: > 0 };

    static ClassDeclarationSyntax GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
        var classDeclarationSyntax = (ClassDeclarationSyntax)context.Node;

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

            if (
                fullName
                is "TheUtils.GenInterfaceAttribute"
                    or "TheUtils.GenerateInterfaceAttribute"
            )
                return classDeclarationSyntax;
        }

        return null;
    }
}
