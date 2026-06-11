using Microsoft.CodeAnalysis;

namespace UITKSourceGenerator
{
    public static class DiagnosticDescriptors
    {
        public static readonly DiagnosticDescriptor ElementNotFound = new(
            id: "UITK001",
            title: "Element not found in UXML",
            messageFormat: "Element '{0}' not found in {1}",
            category: "UITKBinding",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor TypeMismatch = new(
            id: "UITK002",
            title: "Element type mismatch",
            messageFormat: "Element '{0}' in {1} is '{2}', expected '{3}'",
            category: "UITKBinding",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ViewModelPropertyNotFound = new(
            id: "UITK003",
            title: "ViewModel property not found",
            messageFormat: "Property '{0}' not found in ViewModel '{1}'",
            category: "UITKBinding",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UxmlFileNotFound = new(
            id: "UITK004",
            title: "UXML file not found",
            messageFormat: "UXML file '{0}' not found for class '{1}'",
            category: "UITKBinding",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
    }
}
