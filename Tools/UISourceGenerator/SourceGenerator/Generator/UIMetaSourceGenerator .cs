using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

[Generator]
public class UIMetaSourceGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var uiWindowType = context.Compilation.GetTypeByMetadataName("TEngine.UIWindow`1");
        var uiWidgetType = context.Compilation.GetTypeByMetadataName("TEngine.UIWidget`1");
        var uiTabWindowType = context.Compilation.GetTypeByMetadataName("TEngine.UITabWindow`1");
        var windowAttribute = context.Compilation.GetTypeByMetadataName("TEngine.WindowAttribute");
        var uiUpdateAttribute = context.Compilation.GetTypeByMetadataName("TEngine.UIUpdateAttribute");

        var registryCode = new ConcurrentBag<string>();
        var candidateClasses = ((SyntaxReceiver)context.SyntaxReceiver).CandidateClasses;

        // 并行处理候选类提升性能
        Parallel.ForEach(candidateClasses, classDecl =>
        {
            var model = context.Compilation.GetSemanticModel(classDecl.SyntaxTree);
            if (model.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol classSymbol) return;

            var (isValidWindow, isValidWidget, isValidTabWindow) = CheckInheritance(classSymbol, uiWindowType, uiWidgetType, uiTabWindowType);

            var isWindowType = isValidWindow || isValidTabWindow;
            if (!isWindowType && !isValidWidget) return;

            var targetBaseType = isWindowType ?
                (isValidWindow ? uiWindowType : uiTabWindowType) :
                uiWidgetType;
            var holderType = GetHolderType(classSymbol, targetBaseType);

            if (holderType.IsGenericType)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        id: "UI002",                                      // 诊断规则唯一标识符[6,7](@ref)
                        title: "Unresolved generic parameter",            // 错误标题
                        messageFormat: "Type {0} uses open generic parameters", // 消息模板[7](@ref)
                        category: "TypeSafety",                           // 分类标签[6](@ref)
                        defaultSeverity: DiagnosticSeverity.Error,        // 错误级别[1](@ref)
                        isEnabledByDefault: true,                         // 必填参数[6,7](@ref)
                        description: "泛型参数必须指定具体类型"),           // 可选描述
                    classDecl.GetLocation(),
                    classSymbol.Name));                                   // 动态插入类型名
                return;
            }

            var registrationCode = BuildRegistrationCode(context, classSymbol, holderType, isWindowType, windowAttribute, uiUpdateAttribute);
            registryCode.Add(registrationCode);
        });

        GenerateSourceFile(context, registryCode);
    }

    private (bool isWindow, bool isWidget, bool isTabWindow) CheckInheritance(
            INamedTypeSymbol symbol,
            INamedTypeSymbol windowType,
            INamedTypeSymbol widgetType,
            INamedTypeSymbol tabWindowType)
    {
        var baseType = symbol.BaseType;
        while (baseType != null)
        {
            if (baseType.IsGenericType && !baseType.IsUnboundGenericType)
            {
                if (SymbolEqualityComparer.Default.Equals(baseType.OriginalDefinition, windowType))
                    return (true, false, false);

                if (SymbolEqualityComparer.Default.Equals(baseType.OriginalDefinition, widgetType))
                    return (false, true, false);

                if (SymbolEqualityComparer.Default.Equals(baseType.OriginalDefinition, tabWindowType))
                    return (false, false, true);
            }
            baseType = baseType.BaseType;
        }
        return (false, false, false);
    }

    private static INamedTypeSymbol GetHolderType(INamedTypeSymbol classSymbol, INamedTypeSymbol targetBaseType)
    {
        var baseType = classSymbol.BaseType;
        while (baseType != null)
        {
            if (SymbolEqualityComparer.Default.Equals(baseType.OriginalDefinition, targetBaseType) &&
                baseType is INamedTypeSymbol constructedType &&
                constructedType.TypeArguments.FirstOrDefault() is INamedTypeSymbol holderType)
            {
                return holderType;
            }
            baseType = baseType.BaseType;
        }
        return classSymbol;
    }

    private string BuildRegistrationCode(GeneratorExecutionContext context,
        INamedTypeSymbol classSymbol, INamedTypeSymbol holderType,
        bool isWindow, INamedTypeSymbol windowAttribute, INamedTypeSymbol uiUpdateAttribute)
    {
        var builder = new StringBuilder();
        var classType = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var holderTypeName = holderType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (isWindow)
        {
            var attribute = classSymbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Equals(windowAttribute, SymbolEqualityComparer.Default) == true);
            var updateAttribute = classSymbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Equals(uiUpdateAttribute, SymbolEqualityComparer.Default) == true);

            if (attribute != null)
            {
                var layer = (UILayerTest)(attribute.ConstructorArguments[0].Value ?? UILayerTest.UI);
                var fullScreen = (bool)(attribute.ConstructorArguments[1].Value ?? false);
                var cacheTime = (int)(attribute.ConstructorArguments[2].Value ?? 0);

                builder.Append($"UIMetaRegistry.Register(typeof({classType}), ")
                      .Append($"typeof({holderTypeName}), ")
                      .Append($"UILayer.{layer}, ")
                      .Append($"{fullScreen.ToString().ToLower()}, ")
                      .Append($"{cacheTime}, ")
                     .Append($"{(updateAttribute != null).ToString().ToLower()});");
            }
            else
            {
                builder.Append($"UIMetaRegistry.Register(typeof({classType}), typeof({holderTypeName}));");
            }
        }
        else
        {
            builder.Append($"UIMetaRegistry.Register(typeof({classType}), typeof({holderTypeName}));");
        }

        return builder.ToString();
    }

    private void GenerateSourceFile(GeneratorExecutionContext context, IEnumerable<string> registrations)
    {
        var sb = new StringBuilder(@"// <auto-generated/>
using System;
using TEngine;

namespace TEngine
{
    internal static class UIMetaRegistryInitializer
    {
#if UNITY_EDITOR
        [UnityEditor.InitializeOnEnterPlayMode]
#else
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
#endif
        private static void RegisterAll()
        {");

        foreach (var code in registrations.Distinct())
        {
            sb.AppendLine().Append($"            {code}");
        }

        sb.Append(@"
        }
    }
}");
        context.AddSource("UIMetaRegistry.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private class SyntaxReceiver : ISyntaxReceiver
    {
        public List<ClassDeclarationSyntax> CandidateClasses { get; } = new();

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is ClassDeclarationSyntax { BaseList: not null } classDecl)
            {
                foreach (var baseType in classDecl.BaseList.Types)
                {
                    // 添加UITabWindow语法过滤
                    if (baseType.Type is GenericNameSyntax genericName &&
                       (genericName.Identifier.ValueText == "UIWindow" ||
                        genericName.Identifier.ValueText == "UIWidget" ||
                        genericName.Identifier.ValueText == "UITabWindow") &&
                        genericName.TypeArgumentList?.Arguments.Count == 1)
                    {
                        CandidateClasses.Add(classDecl);
                        break;
                    }
                }
            }
        }
    }
}