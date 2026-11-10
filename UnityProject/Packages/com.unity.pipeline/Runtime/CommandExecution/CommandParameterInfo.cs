using System;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// Information about a CLI command parameter.
    /// Contains metadata needed for parameter validation and CLI help generation.
    /// </summary>
    public class CommandParameterInfo
    {
        /// <summary>
        /// Name of the parameter for CLI arguments.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Human-readable description of the parameter.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Whether this parameter is required for command execution.
        /// </summary>
        public bool Required { get; }

        /// <summary>
        /// Type of the parameter for validation and conversion.
        /// </summary>
        public Type ParameterType { get; }

        /// <summary>
        /// Default value for optional parameters.
        /// </summary>
        public object DefaultValue { get; }

        /// <summary>
        /// Create parameter information from discovery.
        /// </summary>
        /// <param name="name">Name of the parameter for CLI arguments.</param>
        /// <param name="description">Human-readable description of the parameter.</param>
        /// <param name="required">Whether this parameter is required for command execution.</param>
        /// <param name="parameterType">Type of the parameter for validation and conversion.</param>
        /// <param name="defaultValue">Default value for optional parameters.</param>
        public CommandParameterInfo(string name, string description, bool required,
            Type parameterType, object defaultValue = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            ParameterType = parameterType ?? throw new ArgumentNullException(nameof(parameterType));
            Required = required;
            DefaultValue = defaultValue;
        }

        /// <summary>A short diagnostic summary of the parameter.</summary>
        /// <returns>The summary string.</returns>
        public override string ToString()
        {
            return $"{Name} Required:{Required} Type:{ParameterType}";
        }

    }
}