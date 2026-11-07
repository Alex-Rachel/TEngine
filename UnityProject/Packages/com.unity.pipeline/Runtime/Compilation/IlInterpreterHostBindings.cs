using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Unity.Pipeline.Compilation
{
    /// <summary>
    /// Shared IlInterpreter <see cref="IlInterpreter.Interpreter.HostBinding"/> surface used by the
    /// interpreter-backed method-reload path. Mirrors IlInterpreter's
    /// <c>UnityHostBinding.Prototyping()</c> minus the Input System block, Audio, and physics —
    /// anything outside UnityEngine's core module binds on demand via <see cref="ResolveAutoBindType"/>,
    /// so the package forces no engine-module dependency. Reference types use <c>AllowType&lt;T&gt;()</c>
    /// and blittable structs <c>AllowTypeStruct&lt;T&gt;()</c> for AOT/IL2CPP safety; static classes
    /// and enums use <c>AllowType(typeof(...))</c>.
    /// </summary>
    static class IlInterpreterHostBindings
    {
// Used by the interpreter-backed method-reload sink; available in dev players (incl. IL2CPP).
#if UNITY_EDITOR || ENABLE_PROFILER || ENABLE_RUNTIME_PIPELINE
        /// <summary>
        /// Single source of truth for the interpreter's host surface: BCL + the standard UnityEngine
        /// types + <c>Debug</c>. Every consumer — the eval binding and the method-reload binding — is a
        /// projection of this one builder, so they cannot drift. Callers overlay only what is
        /// site-specific: eval overlays capturing <c>Debug.Log</c> variants; method reload overlays
        /// <c>AllowType(targetType)</c>.
        /// </summary>
        public static IlInterpreter.Interpreter.HostBinding CreateStandard()
        {
            // Route IlInterpreter registration warnings to the Unity console (the default sink is
            // stderr, which Unity players never surface).
            IlInterpreter.Interpreter.HostBinding.Warn = msg => Debug.LogWarning(msg);

            var binding = AddStandardUnity(new IlInterpreter.Interpreter.HostBinding().AllowBcl())
                .AllowType(typeof(Debug)); // logging from eval/reloaded bodies (eval overlays capture on top)
            // Demand-time auto-bind: the interpreter resolves every TypeRef a loaded script carries
            // through this policy, so engine/game types outside the curated surface bind on first use
            // instead of throwing "not registered". The curated list above stays the fast-path core
            // (typed delegates, flat structs) — a floor, not a ceiling.
            // Types the policy declines (see ResolveAutoBindType) are not bindable — the
            // project-extension seam that could promote them was deferred to a future release.
            binding.AutoBindResolver = ResolveAutoBindType;
            return binding;
        }

        /// <summary>
        /// Auto-bind policy for <see cref="CreateStandard"/>: resolve a CLR full name from the loaded
        /// assemblies, skipping (a) the BCL — the hand-rolled <c>AllowBcl</c> shims collapse doubles
        /// into the script's float number space and must stay authoritative — and (b) this package's
        /// own assemblies. On IL2CPP dev players this self-limits to the types and members the
        /// stripper kept — i.e. what the built game itself references, plus anything the project
        /// preserves through its own <c>link.xml</c>.
        /// </summary>
        static Type ResolveAutoBindType(string fullName)
        {
            foreach (var asm in PipelineUtils.GetLoadedAssemblies())
            {
                if (IsAutoBindSkippedAssembly(asm.GetName().Name ?? string.Empty)) continue;
                Type t = null;
                try { t = asm.GetType(fullName, throwOnError: false); } catch { }
                if (t != null) return t;
            }
            return null;
        }

        // Internal for tests: the classification itself is the contract (a misclassified game
        // assembly silently loses auto-bind), and no test can load a real assembly by these names.
        internal static bool IsAutoBindSkippedAssembly(string name) =>
            name == "System" ||
            name.StartsWith("System.", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.", StringComparison.Ordinal) ||
            name.StartsWith("Mono.", StringComparison.Ordinal) ||
            name == "mscorlib" ||
            name == "netstandard" ||
            name.StartsWith("nunit.", StringComparison.Ordinal) ||
            name.StartsWith("Unity.Pipeline", StringComparison.Ordinal);

        /// <summary>
        /// Register the standard UnityEngine type surface onto <paramref name="binding"/> (fluent).
        /// Private: <see cref="CreateStandard"/> is the only supported way to build the surface,
        /// closing the compose-it-yourself seam where per-site drift used to enter.
        /// </summary>
        static IlInterpreter.Interpreter.HostBinding AddStandardUnity(IlInterpreter.Interpreter.HostBinding binding)
        {
            return binding
                .AllowType(typeof(Resources))
                .AllowType(typeof(PrimitiveType))
                // The base Object surface carries the fake-null operators (op_Equality,
                // op_Implicit -> bool) every `target == null` / `if (component)` guard compiles
                // against, plus Destroy/Instantiate/name. Explicit, not auto-bound: the guards
                // must work on IL2CPP without relying on the on-demand resolver.
                .AllowType<UnityEngine.Object>()
                .AllowType<GameObject>()
                .AllowType<Transform>()
                .AllowType<Camera>()
                .AllowType<Renderer>()
                .AllowType<MeshRenderer>()
                .AllowType<SpriteRenderer>()
                .AllowType<LineRenderer>()
                .AllowType<TrailRenderer>()
                .AllowType<Material>()
                .AllowType<TextMesh>()
                .AllowType<Sprite>()
                .AllowType(typeof(Shader))             // Shader.Find for material creation
                .AllowType(typeof(CameraClearFlags))   // cam.clearFlags
                .AllowType(typeof(TextAnchor))         // TextMesh.anchor
                .AllowType(typeof(TextAlignment))      // TextMesh.alignment
                .AllowType<Vector2>()
                .AllowTypeStruct<Vector3>()
                .AllowType<Quaternion>()
                .AllowType<Color>()
                .AllowType(typeof(Mathf))
                .AllowType(typeof(Time))
                // 3D physics (Rigidbody, Collider, Physics, ForceMode, RaycastHit, ...) is NOT
                // registered here: those types live in UnityEngine.PhysicsModule, and a compile-time
                // reference would force a physics module dependency on every consumer. They bind on
                // demand through AutoBindResolver instead — reflection-tier, and present in a player
                // only when the game's own code kept the module alive.
                .AllowType(typeof(Space))
                .AllowType<Ray>()
                .AllowType(typeof(LayerMask))
                .AllowType(typeof(Cursor))
                .AllowType(typeof(CursorLockMode))
                .AllowType(typeof(Screen))
                .AllowType(typeof(Rect))
                .AllowType(typeof(ScreenOrientation))
                .AllowType(typeof(Application))
                .AllowType(typeof(PlayerPrefs))
                .AllowType(typeof(Color32))
                .AllowType(typeof(System.Collections.IEnumerator))
                .AllowType(typeof(UnityEngine.Random))
                // UITK value-changed callbacks read evt.newValue/previousValue on ChangeEvent<T>.
                // Those are members on an OPEN generic type, which auto-bind can't resolve at Load
                // (there's no closed instantiation to invoke against) — so reflect on the receiver's
                // concrete type at call time. Covers Slider/Toggle/TextField/DropdownField/... the
                // whole RegisterValueChangedCallback surface, without a compile-time UITK dependency.
                .Allow("ChangeEvent`1", "get_newValue",
                    (recv, _) => recv?.GetType().GetProperty("newValue")?.GetValue(recv), 0)
                .Allow("ChangeEvent`1", "get_previousValue",
                    (recv, _) => recv?.GetType().GetProperty("previousValue")?.GetValue(recv), 0)
                // AllowType registers a type's methods/fields/props but not its constructors, so the
                // common ctors are wired explicitly (Vector3's comes from AllowTypeStruct above).
                .AllowConstructor("Vector2",
                    (_, args) => (object)new Vector2(Convert.ToSingle(args[0]), Convert.ToSingle(args[1])), 2)
                .AllowConstructor("Color",
                    (_, args) => (object)new Color(Convert.ToSingle(args[0]), Convert.ToSingle(args[1]), Convert.ToSingle(args[2]), Convert.ToSingle(args[3])), 4)
                .AllowConstructor("GameObject", (_, args) => (object)new GameObject(), 0)
                .AllowConstructor("GameObject", (_, args) => (object)new GameObject((string)args[0]), 1)
                .AllowConstructor("Material", (_, args) => (object)new Material((Shader)args[0]), 1);
        }
#endif
    }
}
