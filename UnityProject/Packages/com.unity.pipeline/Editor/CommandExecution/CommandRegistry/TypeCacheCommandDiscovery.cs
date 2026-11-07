using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Pipeline.Commands;
using UnityEditor;

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// Editor implementation of command discovery using Unity's TypeCache.
    /// Provides fast command discovery in Editor mode via Unity's optimized caching system.
    /// </summary>
    class TypeCacheCommandDiscovery : ICommandDiscovery
    {
        /// <summary>
        /// Find all methods marked with specified attribute using TypeCache.
        /// </summary>
        /// <typeparam name="T">The attribute type to search for.</typeparam>
        /// <returns>The matching methods.</returns>
        public IEnumerable<MethodInfo> GetMethodsWithAttribute<T>() where T : Attribute
        {
            return TypeCache.GetMethodsWithAttribute<T>();
        }
    }
}