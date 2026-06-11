using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace UITKSourceGenerator
{
    [Generator]
    public class UITKBindingGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var classDeclarations = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: (node, _) => node is ClassDeclarationSyntax cds && cds.Modifiers.Any(SyntaxKind.PartialKeyword),
                    transform: (ctx, _) => GetClassInfo(ctx))
                .Where(info => info != null);

            var uxmlFiles = context.AdditionalTextsProvider
                .Where(f => f.Path.EndsWith(".uxml"));

            var combined = classDeclarations.Collect().Combine(uxmlFiles.Collect());

            context.RegisterSourceOutput(combined, (spc, source) =>
            {
                var (classes, uxmls) = source;
                foreach (var classInfo in classes)
                {
                    if (classInfo == null) continue;
                    GenerateBindingCode(spc, classInfo, uxmls);
                }
            });
        }

        private static ClassBindingInfo GetClassInfo(GeneratorSyntaxContext ctx)
        {
            var classDecl = (ClassDeclarationSyntax)ctx.Node;
            var model = ctx.SemanticModel;
            var classSymbol = model.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            if (classSymbol == null) return null;

            if (!InheritsFrom(classSymbol, "UITKWindow") && !InheritsFrom(classSymbol, "UITKWidget"))
                return null;

            var info = new ClassBindingInfo
            {
                ClassName = classSymbol.Name,
                Namespace = classSymbol.ContainingNamespace?.ToDisplayString() ?? "",
                QFields = new List<QFieldInfo>(),
                OnClickMethods = new List<EventMethodInfo>(),
                OnChangeMethods = new List<EventMethodInfo>(),
                BindFields = new List<BindFieldInfo>(),
                BindCommands = new List<BindCommandInfo>(),
            };

            foreach (var member in classSymbol.GetMembers())
            {
                if (member is IFieldSymbol field)
                {
                    foreach (var attr in field.GetAttributes())
                    {
                        if (attr.AttributeClass?.Name == "QAttribute")
                        {
                            string explicitName = attr.ConstructorArguments.Length > 0
                                ? attr.ConstructorArguments[0].Value as string : null;
                            info.QFields.Add(new QFieldInfo
                            {
                                FieldName = field.Name,
                                TypeName = field.Type.Name,
                                ExplicitName = explicitName,
                                UxmlName = explicitName ?? NamingConventions.ToKebabCase(field.Name),
                            });
                        }
                        if (attr.AttributeClass?.Name == "BindAttribute")
                        {
                            var args = attr.ConstructorArguments;
                            info.BindFields.Add(new BindFieldInfo
                            {
                                FieldName = field.Name,
                                TypeName = field.Type.Name,
                                Path = args.Length > 0 ? args[0].Value as string : "",
                                Mode = args.Length > 1 ? (int)args[1].Value : 0,
                                Format = args.Length > 2 ? args[2].Value as string : null,
                            });
                        }
                        if (attr.AttributeClass?.Name == "BindCommandAttribute")
                        {
                            info.BindCommands.Add(new BindCommandInfo
                            {
                                FieldName = field.Name,
                                CommandName = attr.ConstructorArguments[0].Value as string,
                            });
                        }
                    }
                }

                if (member is IMethodSymbol method)
                {
                    foreach (var attr in method.GetAttributes())
                    {
                        if (attr.AttributeClass?.Name == "OnClickAttribute")
                        {
                            string target = attr.ConstructorArguments.Length > 0
                                ? attr.ConstructorArguments[0].Value as string : null;
                            info.OnClickMethods.Add(new EventMethodInfo
                            {
                                MethodName = method.Name,
                                ExplicitTarget = target,
                                UxmlTarget = target ?? NamingConventions.MethodNameToTarget(method.Name),
                            });
                        }
                        if (attr.AttributeClass?.Name == "OnChangeAttribute")
                        {
                            string target = attr.ConstructorArguments[0].Value as string;
                            info.OnChangeMethods.Add(new EventMethodInfo
                            {
                                MethodName = method.Name,
                                ExplicitTarget = target,
                                UxmlTarget = target,
                            });
                        }
                    }
                }
            }

            if (info.QFields.Count == 0 && info.OnClickMethods.Count == 0 &&
                info.OnChangeMethods.Count == 0 && info.BindFields.Count == 0 && info.BindCommands.Count == 0)
                return null;

            return info;
        }

        private static void GenerateBindingCode(SourceProductionContext spc, ClassBindingInfo info, ImmutableArray<AdditionalText> uxmls)
        {
            string uxmlFileName = info.ClassName + ".uxml";
            var uxmlFile = uxmls.FirstOrDefault(f => Path.GetFileName(f.Path) == uxmlFileName);
            List<UXMLElement> uxmlElements = null;

            if (uxmlFile != null)
            {
                string content = uxmlFile.GetText()?.ToString();
                uxmlElements = UXMLParser.Parse(content);

                foreach (var qField in info.QFields)
                {
                    bool found = uxmlElements.Any(e => e.Name == qField.UxmlName);
                    if (!found)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.ElementNotFound,
                            Location.None,
                            qField.UxmlName, uxmlFileName));
                    }
                }
            }
            else if (info.QFields.Count > 0)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.UxmlFileNotFound,
                    Location.None,
                    uxmlFileName, info.ClassName));
            }

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("using UnityEngine.UIElements;");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(info.Namespace))
            {
                sb.AppendLine($"namespace {info.Namespace}");
                sb.AppendLine("{");
            }

            sb.AppendLine($"    partial class {info.ClassName}");
            sb.AppendLine("    {");

            // __UITKAutoBind
            sb.AppendLine("        private void __UITKAutoBind(VisualElement root)");
            sb.AppendLine("        {");
            foreach (var q in info.QFields)
            {
                sb.AppendLine($"            {q.FieldName} = root.Q<{q.TypeName}>(\"{q.UxmlName}\");");
            }
            sb.AppendLine("        }");
            sb.AppendLine();

            // __UITKAutoBindEvents
            sb.AppendLine("        private void __UITKAutoBindEvents()");
            sb.AppendLine("        {");
            foreach (var click in info.OnClickMethods)
            {
                var field = info.QFields.FirstOrDefault(q => q.UxmlName == click.UxmlTarget);
                if (field != null)
                    sb.AppendLine($"            {field.FieldName}.clicked += {click.MethodName};");
            }
            foreach (var change in info.OnChangeMethods)
            {
                var field = info.QFields.FirstOrDefault(q => q.UxmlName == change.UxmlTarget);
                if (field != null)
                    sb.AppendLine($"            {field.FieldName}.RegisterValueChangedCallback({change.MethodName});");
            }
            sb.AppendLine("        }");
            sb.AppendLine();

            // __UITKAutoUnbindEvents
            sb.AppendLine("        private void __UITKAutoUnbindEvents()");
            sb.AppendLine("        {");
            foreach (var click in info.OnClickMethods)
            {
                var field = info.QFields.FirstOrDefault(q => q.UxmlName == click.UxmlTarget);
                if (field != null)
                    sb.AppendLine($"            {field.FieldName}.clicked -= {click.MethodName};");
            }
            foreach (var change in info.OnChangeMethods)
            {
                var field = info.QFields.FirstOrDefault(q => q.UxmlName == change.UxmlTarget);
                if (field != null)
                    sb.AppendLine($"            {field.FieldName}.UnregisterValueChangedCallback({change.MethodName});");
            }
            sb.AppendLine("        }");

            sb.AppendLine("    }");

            if (!string.IsNullOrEmpty(info.Namespace))
            {
                sb.AppendLine("}");
            }

            spc.AddSource($"{info.ClassName}.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        private static bool InheritsFrom(INamedTypeSymbol symbol, string baseName)
        {
            var current = symbol.BaseType;
            while (current != null)
            {
                if (current.Name == baseName) return true;
                current = current.BaseType;
            }
            return false;
        }
    }

    internal class ClassBindingInfo
    {
        public string ClassName;
        public string Namespace;
        public List<QFieldInfo> QFields;
        public List<EventMethodInfo> OnClickMethods;
        public List<EventMethodInfo> OnChangeMethods;
        public List<BindFieldInfo> BindFields;
        public List<BindCommandInfo> BindCommands;
    }

    internal class QFieldInfo
    {
        public string FieldName;
        public string TypeName;
        public string ExplicitName;
        public string UxmlName;
    }

    internal class EventMethodInfo
    {
        public string MethodName;
        public string ExplicitTarget;
        public string UxmlTarget;
    }

    internal class BindFieldInfo
    {
        public string FieldName;
        public string TypeName;
        public string Path;
        public int Mode;
        public string Format;
    }

    internal class BindCommandInfo
    {
        public string FieldName;
        public string CommandName;
    }
}
