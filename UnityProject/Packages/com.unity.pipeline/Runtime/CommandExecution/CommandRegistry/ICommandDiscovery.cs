using System.Collections.Generic;
using System.Reflection;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// Interface for command discovery mechanism.
    /// Allows Runtime assembly to discover commands without directly using Editor APIs.
    /// </summary>
    interface ICommandDiscovery
    {
        /// <summary>
        /// Find all methods marked with CliGroupCommand or CliCommand attribute.
        /// Implementation uses TypeCache in Editor or reflection fallback in Runtime.
        /// </summary>
        /// <typeparam name="T">The attribute type to search for.</typeparam>
        /// <returns>The matching methods.</returns>
        IEnumerable<MethodInfo> GetMethodsWithAttribute<T>() where T : System.Attribute;
    }
}