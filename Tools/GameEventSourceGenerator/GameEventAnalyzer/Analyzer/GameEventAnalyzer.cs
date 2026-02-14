using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
namespace EventAnalyzer;

/// <summary>
/// 游戏事件分析器
/// <remarks>用于在编译时检测 事件监听方法 调用的泛型参数</remarks>
/// <remarks>是否与对应接口方法的参数类型一致</remarks>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GameEventAnalyzer : DiagnosticAnalyzer
{
    #region 诊断规则定义

    /// <summary>
    /// 参数类型不匹配规则
    /// <remarks>当泛型参数类型与接口方法参数类型不一致时触发</remarks>
    /// </summary>
    private static readonly DiagnosticDescriptor m_ruleTypeMismatch = new DiagnosticDescriptor(
        Definition.DiagnosticId_TypeMatch, // 诊断ID（唯一标识符）
        Definition.TitleTypeMatch, // 标题（简短描述）
        Definition.MessageFormatTypeMatch, // 错误消息模板（支持格式化参数）
        Definition.Category, // 类别（用于分组）
        DiagnosticSeverity.Error, // 严重级别
        isEnabledByDefault: true, // 是否默认启用
        description: Definition.DescriptionTypeMatch); // 详细说明（可选）

    /// <summary>
    /// 参数数量不匹配规则
    /// <remarks>当泛型参数数量与接口方法参数数量不一致时触发</remarks>
    /// </summary>
    private static readonly DiagnosticDescriptor m_ruleParamCountMismatch = new DiagnosticDescriptor(
        Definition.DiagnosticId_ParamCount,
        Definition.TitleParamCount,
        Definition.MessageFormatParamCount,
        Definition.Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Definition.DescriptionParamCount);

    #endregion

    /// <summary>
    /// 返回此分析器支持的所有诊断规则
    /// </summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(m_ruleTypeMismatch, m_ruleParamCountMismatch);

    /// <summary>
    /// 初始化分析器，注册语法节点分析回调
    /// </summary>
    /// <param name="context">分析上下文</param>
    public override void Initialize(AnalysisContext context)
    {
        // 不分析自动生成的代码
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        // 启用并发执行以提高性能
        context.EnableConcurrentExecution();
        // 注册方法调用表达式的分析回调
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    /// <summary>
    /// 分析方法调用表达式
    /// </summary>
    /// <remarks>检测 事件监听方法 调用的泛型参数是否与接口方法参数匹配</remarks>
    /// <param name="context">语法节点分析上下文</param>
    private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // 获取方法符号
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);

        if (!(symbolInfo.Symbol is IMethodSymbol methodSymbol))
        {
            return;
        }

        // 检查是否是 Definition 包含的事件监听调用方法
        if (!Definition.CheckMethodNameList.Contains(methodSymbol.Name))
        {
            return;
        }

        // 获取泛型参数
        var typeArguments = methodSymbol.TypeArguments;

        // 获取第一个参数（事件ID）
        var arguments = invocation.ArgumentList.Arguments;

        if (arguments.Count == 0)
        {
            return;
        }

        var firstArg = arguments[0].Expression;

        // 解析事件ID参数，获取接口名和方法名
        if (!AnalyzerHelper.TryParseEventId(firstArg, context.SemanticModel, out var interfaceName, out var methodName,
                out var eventClassName))
        {
            return;
        }

        // 查找对应的接口
        var interfaceSymbol = AnalyzerHelper.FindInterface(context.Compilation, interfaceName, eventClassName);

        if (interfaceSymbol == null)
        {
            return;
        }

        // 查找对应的方法
        var interfaceMethod = interfaceSymbol.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .FirstOrDefault();

        if (interfaceMethod == null)
        {
            return;
        }

        // 获取接口方法的参数类型
        var parameterTypes = interfaceMethod.Parameters.Select(p => p.Type).ToList();

        // 检查参数数量是否匹配
        if (typeArguments.Length != parameterTypes.Count)
        {
            var diagnostic = Diagnostic.Create(
                m_ruleParamCountMismatch, // 诊断规则描述符
                invocation.GetLocation(), // 错误位置
                typeArguments.Length, // 调用的参数数量
                interfaceName, // "ILoginUI"
                methodName, // "Test"
                parameterTypes.Count); // 原始方法参数数量

            context.ReportDiagnostic(diagnostic);
            return;
        }

        // 逐个比较参数类型
        for (int i = 0; i < typeArguments.Length; i++)
        {
            var actualType = typeArguments[i];
            var expectedType = parameterTypes[i];

            if (!SymbolEqualityComparer.Default.Equals(actualType, expectedType))
            {
                var diagnostic = Diagnostic.Create(
                    m_ruleTypeMismatch, // 诊断规则描述符
                    invocation.GetLocation(), // 错误位置
                    AnalyzerHelper.GetTypeName(actualType), // 实际的参数类型
                    interfaceName, // "ILoginUI"
                    methodName, // "Test"
                    AnalyzerHelper.GetTypeName(expectedType), // 期望的参数类型
                    i + 1); // 参数位置（从1开始）

                context.ReportDiagnostic(diagnostic);
            }
        }
    }
}