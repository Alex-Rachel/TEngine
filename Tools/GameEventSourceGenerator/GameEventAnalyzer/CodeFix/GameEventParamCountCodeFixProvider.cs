using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EventAnalyzer;

/// <summary>
/// 事件参数数量不匹配的代码修复器
/// <remarks>为 EVENT002 诊断提供自动修复功能</remarks>
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(GameEventParamCountCodeFixProvider)), Shared]
public class GameEventParamCountCodeFixProvider : CodeFixProvider
{
    /// <summary>
    /// 修复操作的标题
    /// </summary>
    private const string m_title = "修复事件参数数量";

    /// <summary>
    /// 此修复器支持的诊断ID列表
    /// </summary>
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(Definition.DiagnosticId_ParamCount);

    /// <summary>
    /// 获取修复所有同类问题的提供器
    /// </summary>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <summary>
    /// 注册代码修复操作
    /// </summary>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        if (root == null)
        {
            return;
        }

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // 查找包含诊断位置的方法调用表达式
        var invocation = root.FindToken(diagnosticSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();

        if (invocation == null)
        {
            return;
        }

        // 注册修复操作
        context.RegisterCodeFix(
            CodeAction.Create(
                title: m_title,
                createChangedDocument: c => FixParamCountAsync(context.Document, invocation, c),
                equivalenceKey: m_title),
            diagnostic);
    }

    /// <summary>
    /// 执行参数数量修复
    /// </summary>
    private async Task<Document> FixParamCountAsync(Document document, InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        if (semanticModel == null)
        {
            return document;
        }

        // 获取方法符号
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);

        if (!(symbolInfo.Symbol is IMethodSymbol methodSymbol))
        {
            return document;
        }

        // 获取第一个参数（事件ID）
        var arguments = invocation.ArgumentList.Arguments;

        if (arguments.Count < 2)
        {
            return document;
        }

        var firstArg = arguments[0].Expression;
        var secondArg = arguments[1].Expression; // 回调方法

        // 解析事件ID参数，获取接口名和方法名
        if (!AnalyzerHelper.TryParseEventId(firstArg, semanticModel, out var interfaceName, out var methodName,
                out var eventClassName))
        {
            return document;
        }

        // 查找对应的接口
        var compilation = semanticModel.Compilation;
        var interfaceSymbol = AnalyzerHelper.FindInterface(compilation, interfaceName, eventClassName);

        if (interfaceSymbol == null)
        {
            return document;
        }

        // 查找对应的方法
        var interfaceMethod = interfaceSymbol.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .FirstOrDefault();

        if (interfaceMethod == null)
        {
            return document;
        }

        // 获取接口方法的参数类型
        var parameterTypes = interfaceMethod.Parameters.Select(p => p.Type).ToList();

        // 构建正确的泛型参数列表
        var correctedTypeArguments = new TypeSyntax[parameterTypes.Count];

        for (int i = 0; i < parameterTypes.Count; i++)
        {
            var typeName = AnalyzerHelper.GetTypeName(parameterTypes[i]);
            correctedTypeArguments[i] = SyntaxFactory.ParseTypeName(typeName);
        }

        // 获取原始表达式并替换泛型参数
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (root == null)
        {
            return document;
        }

        // 获取接口方法的参数信息
        var parameterInfos = AnalyzerHelper.GetParameterInfos(interfaceMethod);

        // 查找回调方法声明
        var callbackMethodDecl = AnalyzerHelper.FindCallbackMethodDeclaration(secondArg, semanticModel, root);

        SyntaxNode newRoot;

        // 处理不同的调用形式
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            // e.g.: obj.Method<T>()
            if (memberAccess.Name is GenericNameSyntax genericName)
            {
                ExpressionSyntax newExpression;

                // 无参情况：移除泛型参数
                if (parameterTypes.Count == 0)
                {
                    var newIdentifierName = SyntaxFactory.IdentifierName(genericName.Identifier);
                    newExpression = memberAccess.WithName(newIdentifierName);
                }
                else
                {
                    var newGenericName = genericName.WithTypeArgumentList(
                        SyntaxFactory.TypeArgumentList(
                            SyntaxFactory.SeparatedList(correctedTypeArguments)));
                    newExpression = memberAccess.WithName(newGenericName);
                }

                var newInvocation = invocation.WithExpression(newExpression);

                // 同时替换调用和回调方法
                if (callbackMethodDecl != null)
                {
                    var fixedCallbackMethod = AnalyzerHelper.FixCallbackMethodParameters(callbackMethodDecl, parameterInfos);
                    newRoot = root.ReplaceNodes(
                        new SyntaxNode[] { invocation, callbackMethodDecl },
                        (original, _) =>
                        {
                            if (original == invocation) return newInvocation;
                            if (original == callbackMethodDecl) return fixedCallbackMethod;
                            return original;
                        });
                }
                else
                {
                    newRoot = root.ReplaceNode(invocation, newInvocation);
                }
            }
            else if (memberAccess.Name is IdentifierNameSyntax identifierName)
            {
                // 没有泛型参数，需要添加
                if (parameterTypes.Count > 0)
                {
                    var newGenericName = SyntaxFactory.GenericName(identifierName.Identifier)
                        .WithTypeArgumentList(
                            SyntaxFactory.TypeArgumentList(
                                SyntaxFactory.SeparatedList(correctedTypeArguments)));

                    var newMemberAccess = memberAccess.WithName(newGenericName);
                    var newInvocation = invocation.WithExpression(newMemberAccess);

                    // 同时替换调用和回调方法
                    if (callbackMethodDecl != null)
                    {
                        var fixedCallbackMethod = AnalyzerHelper.FixCallbackMethodParameters(callbackMethodDecl, parameterInfos);
                        newRoot = root.ReplaceNodes(
                            new SyntaxNode[] { invocation, callbackMethodDecl },
                            (original, _) =>
                            {
                                if (original == invocation) return newInvocation;
                                if (original == callbackMethodDecl) return fixedCallbackMethod;
                                return original;
                            });
                    }
                    else
                    {
                        newRoot = root.ReplaceNode(invocation, newInvocation);
                    }
                }
                else
                {
                    // 不需要泛型参数，只修复回调方法
                    if (callbackMethodDecl != null)
                    {
                        var fixedCallbackMethod = AnalyzerHelper.FixCallbackMethodParameters(callbackMethodDecl, parameterInfos);
                        newRoot = root.ReplaceNode(callbackMethodDecl, fixedCallbackMethod);
                    }
                    else
                    {
                        return document;
                    }
                }
            }
            else
            {
                return document;
            }
        }
        else if (invocation.Expression is GenericNameSyntax directGenericName)
        {
            // e.g.: Method<T>()
            ExpressionSyntax newExpression;

            // 无参情况：移除泛型参数
            if (parameterTypes.Count == 0)
            {
                newExpression = SyntaxFactory.IdentifierName(directGenericName.Identifier);
            }
            else
            {
                newExpression = directGenericName.WithTypeArgumentList(
                    SyntaxFactory.TypeArgumentList(
                        SyntaxFactory.SeparatedList(correctedTypeArguments)));
            }

            var newInvocation = invocation.WithExpression(newExpression);

            // 同时替换调用和回调方法
            if (callbackMethodDecl != null)
            {
                var fixedCallbackMethod = AnalyzerHelper.FixCallbackMethodParameters(callbackMethodDecl, parameterInfos);
                newRoot = root.ReplaceNodes(
                    new SyntaxNode[] { invocation, callbackMethodDecl },
                    (original, _) =>
                    {
                        if (original == invocation) return newInvocation;
                        if (original == callbackMethodDecl) return fixedCallbackMethod;
                        return original;
                    });
            }
            else
            {
                newRoot = root.ReplaceNode(invocation, newInvocation);
            }
        }
        else if (invocation.Expression is IdentifierNameSyntax identifierName)
        {
            // e.g.: Method() 没有泛型参数
            if (parameterTypes.Count > 0)
            {
                var newGenericName = SyntaxFactory.GenericName(identifierName.Identifier)
                    .WithTypeArgumentList(
                        SyntaxFactory.TypeArgumentList(
                            SyntaxFactory.SeparatedList(correctedTypeArguments)));

                var newInvocation = invocation.WithExpression(newGenericName);

                // 同时替换调用和回调方法
                if (callbackMethodDecl != null)
                {
                    var fixedCallbackMethod = AnalyzerHelper.FixCallbackMethodParameters(callbackMethodDecl, parameterInfos);
                    newRoot = root.ReplaceNodes(
                        new SyntaxNode[] { invocation, callbackMethodDecl },
                        (original, _) =>
                        {
                            if (original == invocation) return newInvocation;
                            if (original == callbackMethodDecl) return fixedCallbackMethod;
                            return original;
                        });
                }
                else
                {
                    newRoot = root.ReplaceNode(invocation, newInvocation);
                }
            }
            else
            {
                // 只修复回调方法
                if (callbackMethodDecl != null)
                {
                    var fixedCallbackMethod = AnalyzerHelper.FixCallbackMethodParameters(callbackMethodDecl, parameterInfos);
                    newRoot = root.ReplaceNode(callbackMethodDecl, fixedCallbackMethod);
                }
                else
                {
                    return document;
                }
            }
        }
        else
        {
            return document;
        }

        return document.WithSyntaxRoot(newRoot);
    }
}