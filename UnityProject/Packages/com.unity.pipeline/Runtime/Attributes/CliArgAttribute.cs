using System;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// Attribute to mark parameters of CLI command methods.
    /// Provides parameter metadata for CLI validation and help generation.
    /// Adapted from unity-tools [Parameter] attribute for Pipeline organization requirements.
    ///
    /// Also valid on the fields/properties of an <c>IStructuredCommandInput</c> DTO, where it
    /// describes a member of a nested object schema (same Name/Description/Required semantics).
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public class CliArgAttribute : Attribute
    {
        /// <summary>
        /// Name of the parameter as it appears in CLI arguments.
        /// Used in "unity request command_name --param_name value" syntax.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Human-readable description of the parameter.
        /// Used in CLI help text and parameter documentation.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Whether this parameter is required for command execution.
        /// Default: false (parameter is optional).
        /// </summary>
        public bool Required { get; set; } = false;

        /// <summary>
        /// Default value for the parameter if not provided.
        /// Must be compatible with the parameter type.
        /// </summary>
        public object DefaultValue { get; set; }

        /// <summary>
        /// Create a new CLI parameter attribute.
        /// </summary>
        /// <param name="name">Parameter name for CLI arguments</param>
        /// <param name="description">Human-readable description</param>
        public CliArgAttribute(string name, string description)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? throw new ArgumentNullException(nameof(description));
        }
    }
}