using System;
using UnityEditor;

namespace Unity.Pipeline.Editor.Authoring
{
    /// <summary>
    /// <para>
    /// Groups all Undo operations registered during its lifetime into a single, collapsible Editor
    /// Undo step, so a multi-step agent action reverts as one. Register mutations inside the scope
    /// with Undo.RegisterCreatedObjectUndo / RegisterCompleteObjectUndo / etc.
    /// </para>
    ///
    /// <code>
    /// using (new AuthoringUndoScope("Create Enemy"))
    /// {
    ///     var go = new GameObject("Enemy");
    ///     Undo.RegisterCreatedObjectUndo(go, "Create Enemy");
    ///     // ... further registered mutations collapse into the same step
    /// }
    /// </code>
    ///
    /// <para>
    /// NOTE: minimal seed for the shared safety policy (CAT-2509). AssetDatabase operations
    /// (folder/asset creation, import) are NOT part of Unity's Undo system, so this scope only
    /// affects scene/object mutations.
    /// </para>
    /// </summary>
    public sealed class AuthoringUndoScope : IDisposable
    {
        private readonly int m_Group;
        private bool m_Canceled;

        /// <summary>Start a new collapsible Undo group.</summary>
        /// <param name="name">Name shown for the collapsed Undo step.</param>
        public AuthoringUndoScope(string name)
        {
            Undo.IncrementCurrentGroup();
            m_Group = Undo.GetCurrentGroup();
            if (!string.IsNullOrEmpty(name))
                Undo.SetCurrentGroupName(name);
        }

        /// <summary>
        /// Skip the collapse on dispose. Call after the scope's registered operations have been
        /// reverted wholesale (e.g. <c>Undo.RevertAllDownToGroup(group)</c> on a transactional batch
        /// abort): the group id no longer refers to this scope's operations on the Undo stack, and
        /// collapsing a discarded id could fold whatever now sits at that index into a stray group.
        /// </summary>
        public void Cancel() => m_Canceled = true;

        /// <summary>Collapse all Undo operations registered during this scope into one step, unless <see cref="Cancel"/> was called.</summary>
        public void Dispose()
        {
            if (!m_Canceled)
                Undo.CollapseUndoOperations(m_Group);
        }
    }
}
