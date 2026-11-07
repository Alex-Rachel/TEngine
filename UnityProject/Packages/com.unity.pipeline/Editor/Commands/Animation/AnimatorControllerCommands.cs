using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands.Animation
{
    /// <summary>
    /// Group B of CLI-214: author <see cref="AnimatorController"/> assets — layers, parameters, states
    /// and transitions. Built on <c>UnityEditor.Animations</c>, which is part of the Editor (no extra
    /// package), so the types are referenced directly.
    ///
    /// Like the animation-clip commands, paths/handles are sandbox-confined via
    /// <see cref="ProjectPaths"/> / <see cref="ObjectResolver"/>, and every command supports
    /// <c>dry-run</c>. Some AnimatorController API calls register Undo internally, but sub-object edits
    /// are not reliably undoable, so changes are persisted with <see cref="EditorUtility.SetDirty"/> +
    /// <see cref="AssetDatabase.SaveAssets"/>.
    ///
    /// Out of scope (v1): BlendTree authoring (assigning an EXISTING BlendTree as a state motion is
    /// allowed), StateMachineBehaviours, sub-state machines, and humanoid/avatar configuration.
    /// </summary>
    static class AnimatorControllerCommands
    {
        const string Group = "animation.animator";

        [CliGroupCommand(Group,
            "Create an .controller AnimatorController asset (with a default Base Layer) under the authoring root.",
            MainThreadRequired = true,
            Tags = new[] { "animation/animator" })]
        public static AuthoringResult CreateController(
            [CliArg("path", "Asset path ending in .controller, relative to the authoring root. The Assets/ prefix is optional.", Required = true)] string path,
            [CliArg("confirm", "Required (true) only when overwriting an existing asset at the path.")] bool confirm = false,
            [CliArg("dry-run", "If true, validate inputs and report what would be created without writing anything.")] bool dryRun = false)
        {
            var normalized = ProjectPaths.Resolve(path, out var error);
            if (normalized == null)
                throw new ArgumentException(error);

            if (!string.Equals(Path.GetExtension(normalized), ".controller", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Animator controller path '{normalized}' must end in .controller.");

            var exists = AssetDatabase.LoadMainAssetAtPath(normalized) != null;
            if (exists && !confirm)
                throw new ArgumentException($"An asset already exists at '{normalized}'. Pass confirm=true to overwrite it.");

            if (dryRun)
                return new AuthoringResult { AssetPath = normalized, Type = nameof(AnimatorController) };

            EnsureParentFolder(normalized);

            if (exists)
                AssetDatabase.DeleteAsset(normalized);

            // CreateAnimatorControllerAtPath creates the asset on disk WITH a default "Base Layer".
            var controller = AnimatorController.CreateAnimatorControllerAtPath(normalized);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            var loaded = AssetDatabase.LoadMainAssetAtPath(normalized);
            var result = ObjectResolver.Describe(loaded) ?? new AuthoringResult { Type = nameof(AnimatorController) };
            result.AssetPath = normalized;
            return result;
        }

        [CliGroupCommand(Group,
            "Add a parameter (Float | Int | Bool | Trigger) to an AnimatorController. A duplicate name returns code 'duplicate_parameter'.",
            MainThreadRequired = true,
            Tags = new[] { "animation/animator" })]
        public static object AddParameter(
            [CliArg("controller", "Reference to the AnimatorController to edit (path / guid / globalId).", Required = true)] ObjectRef controller,
            [CliArg("name", "Parameter name.", Required = true)] string name,
            [CliArg("type", "Parameter type: Float | Int | Bool | Trigger.", Required = true)] string type = null,
            [CliArg("default-value", "Default value for Float/Int/Bool (ignored for Trigger).")] JToken defaultValue = null,
            [CliArg("dry-run", "If true, validate inputs without writing the parameter.")] bool dryRun = false)
        {
            var (ctrl, ctrlPath) = ResolveController(controller);
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required.");

            var paramType = ParseParameterType(type);

            if (ctrl.parameters.Any(p => p.name == name))
                return ErrorResult.PackageStyle("duplicate_parameter", $"A parameter named '{name}' already exists on '{ctrlPath}'.");

            // Resolve (and validate) the default value up front so dry-run also fails on a bad value.
            float defFloat = 0f;
            int defInt = 0;
            bool defBool = false;
            switch (paramType)
            {
                case AnimatorControllerParameterType.Float:
                    if (defaultValue != null && defaultValue.Type != JTokenType.Null) defFloat = defaultValue.ToObject<float>();
                    break;
                case AnimatorControllerParameterType.Int:
                    if (defaultValue != null && defaultValue.Type != JTokenType.Null) defInt = defaultValue.ToObject<int>();
                    break;
                case AnimatorControllerParameterType.Bool:
                    if (defaultValue != null && defaultValue.Type != JTokenType.Null) defBool = defaultValue.ToObject<bool>();
                    break;
            }

            var result = new AddParameterResult
            {
                AssetPath = ctrlPath,
                Type = nameof(AnimatorController),
                Parameter = new ParameterInfo
                {
                    Name = name,
                    ParamType = paramType.ToString(),
                    DefaultValue = DefaultValueToken(paramType, defFloat, defInt, defBool)
                }
            };

            if (dryRun)
                return result;

            var parameter = new AnimatorControllerParameter
            {
                name = name,
                type = paramType,
                defaultFloat = defFloat,
                defaultInt = defInt,
                defaultBool = defBool
            };
            ctrl.AddParameter(parameter);

            Persist(ctrl);
            FillIdentity(result, ctrl);
            return result;
        }

        [CliGroupCommand(Group,
            "Add a layer to an AnimatorController.",
            MainThreadRequired = true,
            Tags = new[] { "animation/animator" })]
        public static object AddLayer(
            [CliArg("controller", "Reference to the AnimatorController to edit (path / guid / globalId).", Required = true)] ObjectRef controller,
            [CliArg("name", "Layer name.", Required = true)] string name,
            [CliArg("weight", "Layer weight (default 1).")] float weight = 1f,
            [CliArg("blending-mode", "Blending mode: Override | Additive (default Override).")] string blendingMode = "Override",
            [CliArg("dry-run", "If true, validate inputs without writing the layer.")] bool dryRun = false)
        {
            var (ctrl, ctrlPath) = ResolveController(controller);
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required.");

            if (!Enum.TryParse<AnimatorLayerBlendingMode>(blendingMode, ignoreCase: true, out var mode))
                throw new ArgumentException($"Unknown blending-mode '{blendingMode}'. Use Override | Additive.");

            var index = ctrl.layers.Length;
            var result = new AddLayerResult
            {
                AssetPath = ctrlPath,
                Type = nameof(AnimatorController),
                Layer = new LayerSummary { Name = name, Index = index, Weight = weight }
            };

            if (dryRun)
                return result;

            // AddLayer(name) appends a layer with a fresh state machine. We then set its weight/blending
            // by writing the layer array back (AnimatorControllerLayer is a value-like wrapper, so the
            // edited copy must be reassigned).
            ctrl.AddLayer(name);
            var layers = ctrl.layers;
            layers[index].defaultWeight = weight;
            layers[index].blendingMode = mode;
            ctrl.layers = layers;

            Persist(ctrl);
            FillIdentity(result, ctrl);
            return result;
        }

        [CliGroupCommand(Group,
            "Add a state to a layer, optionally with a motion (AnimationClip or BlendTree) and as the layer default. " +
            "A layer name with no match returns code 'layer_not_found'.",
            MainThreadRequired = true,
            Tags = new[] { "animation/animator" })]
        public static object AddState(
            [CliArg("controller", "Reference to the AnimatorController to edit (path / guid / globalId).", Required = true)] ObjectRef controller,
            [CliArg("layer", "Layer index (int) or name (string). Default 0 (Base Layer).")] JToken layer = null,
            [CliArg("name", "State name.", Required = true)] string name = null,
            [CliArg("motion", "Optional AnimationClip or BlendTree asset to assign as the state's motion.")] ObjectRef motion = null,
            [CliArg("is-default", "If true, set this state as the layer's default state.")] bool isDefault = false,
            [CliArg("position", "Optional [x, y] node position in the graph (cosmetic).")] JArray position = null,
            [CliArg("dry-run", "If true, validate inputs without writing the state.")] bool dryRun = false)
        {
            var (ctrl, ctrlPath) = ResolveController(controller);
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required.");

            if (!TryResolveLayer(ctrl, layer, out var layerIndex, out var layerError))
                return ErrorResult.PackageStyle("layer_not_found", layerError);

            // Resolve an optional motion. Only AnimationClip / Motion (BlendTree derives from Motion) are
            // valid. A handle that doesn't resolve to a Motion is a clear error.
            UnityEngine.Motion resolvedMotion = null;
            if (motion != null && !motion.IsEmpty)
            {
                if (!ObjectResolver.TryResolve(motion, out var motionObj, out var motionError))
                    throw new ArgumentException($"Could not resolve motion: {motionError}");
                resolvedMotion = motionObj as UnityEngine.Motion;
                if (resolvedMotion == null)
                    throw new ArgumentException($"Motion reference '{motion}' resolved to a {motionObj.GetType().Name}, not an AnimationClip or BlendTree.");

                // Re-confine the motion to the authoring root: a restricted root must not be bypassed by
                // referencing an AnimationClip/BlendTree that lives outside the sandbox.
                ConfineToAuthoringRoot(resolvedMotion, "Motion", motion);
            }

            Vector3? nodePosition = null;
            if (position != null && position.Count > 0)
            {
                if (position.Count < 2)
                    throw new ArgumentException("position must be [x, y].");
                nodePosition = new Vector3(position[0].ToObject<float>(), position[1].ToObject<float>(), 0f);
            }

            var result = new AddStateResult
            {
                AssetPath = ctrlPath,
                Type = nameof(AnimatorController),
                State = new StateSummary
                {
                    Name = name,
                    Layer = layerIndex,
                    HasMotion = resolvedMotion != null,
                    IsDefault = isDefault
                }
            };

            if (dryRun)
                return result;

            var stateMachine = ctrl.layers[layerIndex].stateMachine;
            var state = nodePosition.HasValue
                ? stateMachine.AddState(name, nodePosition.Value)
                : stateMachine.AddState(name);

            if (resolvedMotion != null)
                state.motion = resolvedMotion;

            if (isDefault)
                stateMachine.defaultState = state;

            Persist(ctrl);
            FillIdentity(result, ctrl);
            return result;
        }

        [CliGroupCommand(Group,
            "Add a transition between two states (or from AnyState/Entry, to Exit) on a layer, with optional conditions. " +
            "Validates that the states exist and each condition's parameter exists and its mode matches the parameter type.",
            MainThreadRequired = true,
            Tags = new[] { "animation/animator" })]
        public static object AddTransition(
            [CliArg("controller", "Reference to the AnimatorController to edit (path / guid / globalId).", Required = true)] ObjectRef controller,
            [CliArg("layer", "Layer index (int) or name (string). Default 0 (Base Layer).")] JToken layer = null,
            [CliArg("from-state", "Source state name, or the special \"AnyState\" / \"Entry\".", Required = true)] string fromState = null,
            [CliArg("to-state", "Destination state name, or the special \"Exit\".", Required = true)] string toState = null,
            [CliArg("conditions", "Optional conditions: [{ parameter, mode: \"If\"|\"IfNot\"|\"Greater\"|\"Less\"|\"Equals\"|\"NotEqual\", threshold? }].")] JArray conditions = null,
            [CliArg("has-exit-time", "If true, the transition uses exit time (default false).")] bool hasExitTime = false,
            [CliArg("exit-time", "Normalized exit time (0..1) when has-exit-time is set.")] float exitTime = 0f,
            [CliArg("duration", "Transition duration in seconds (default 0.25).")] float duration = 0.25f,
            [CliArg("has-fixed-duration", "If true, duration is in seconds; otherwise normalized (default true).")] bool hasFixedDuration = true,
            [CliArg("dry-run", "If true, validate everything (states, parameters, mode/type) without writing the transition.")] bool dryRun = false)
        {
            var (ctrl, ctrlPath) = ResolveController(controller);
            if (string.IsNullOrWhiteSpace(fromState))
                throw new ArgumentException("from-state is required.");
            if (string.IsNullOrWhiteSpace(toState))
                throw new ArgumentException("to-state is required.");

            // exitTime is normalized (0..1) and only meaningful with hasExitTime; duration must be
            // non-negative. Reject out-of-range values up front (writing nothing) rather than letting
            // Unity clamp them unpredictably.
            if (hasExitTime && (exitTime < 0f || exitTime > 1f))
                throw new ArgumentException($"exit-time must be in [0, 1] when has-exit-time is set (got {exitTime}).");
            if (duration < 0f)
                throw new ArgumentException($"duration must be >= 0 (got {duration}).");

            if (!TryResolveLayer(ctrl, layer, out var layerIndex, out var layerError))
                return ErrorResult.PackageStyle("layer_not_found", layerError);

            var stateMachine = ctrl.layers[layerIndex].stateMachine;

            const string AnyState = "AnyState";
            const string Entry = "Entry";
            const string Exit = "Exit";

            var fromIsAny = string.Equals(fromState, AnyState, StringComparison.OrdinalIgnoreCase);
            var fromIsEntry = string.Equals(fromState, Entry, StringComparison.OrdinalIgnoreCase);
            var toIsExit = string.Equals(toState, Exit, StringComparison.OrdinalIgnoreCase);

            // Resolve the source state (unless it's AnyState/Entry, which are nodes on the machine).
            AnimatorState fromAnimState = null;
            if (!fromIsAny && !fromIsEntry)
            {
                fromAnimState = FindState(stateMachine, fromState);
                if (fromAnimState == null)
                    throw new ArgumentException($"Source state '{fromState}' not found in layer {layerIndex}.");
            }

            // Resolve the destination state (unless it's Exit).
            AnimatorState toAnimState = null;
            if (!toIsExit)
            {
                toAnimState = FindState(stateMachine, toState);
                if (toAnimState == null)
                    throw new ArgumentException($"Destination state '{toState}' not found in layer {layerIndex}.");
            }

            // Entry transitions cannot carry conditions/exit-time the same way; restrict combinations the
            // engine forbids so an agent gets a clear error rather than a malformed transition.
            if (fromIsEntry && toIsExit)
                throw new ArgumentException("A transition from Entry directly to Exit is not supported.");

            // Validate conditions BEFORE creating anything (so a bad condition writes nothing).
            var parsedConditions = ParseConditions(conditions, ctrl);

            var result = new AddTransitionResult
            {
                AssetPath = ctrlPath,
                Type = nameof(AnimatorController),
                Transition = new TransitionSummary
                {
                    From = fromState,
                    To = toState,
                    ConditionCount = parsedConditions.Count
                }
            };

            if (dryRun)
                return result;

            // Create the transition node matching the from/to kinds.
            AnimatorStateTransition stateTransition = null;
            AnimatorTransition entryTransition = null;

            if (fromIsEntry)
            {
                // Entry -> state. Entry transitions are plain AnimatorTransition (no exit time/duration).
                entryTransition = stateMachine.AddEntryTransition(toAnimState);
            }
            else if (fromIsAny)
            {
                stateTransition = toIsExit ? null : stateMachine.AddAnyStateTransition(toAnimState);
                if (stateTransition == null)
                    throw new ArgumentException("An AnyState transition to Exit is not supported.");
            }
            else
            {
                stateTransition = toIsExit
                    ? fromAnimState.AddExitTransition()
                    : fromAnimState.AddTransition(toAnimState);
            }

            if (stateTransition != null)
            {
                stateTransition.hasExitTime = hasExitTime;
                stateTransition.exitTime = exitTime;
                stateTransition.hasFixedDuration = hasFixedDuration;
                stateTransition.duration = duration;
                foreach (var c in parsedConditions)
                    stateTransition.AddCondition(c.Mode, c.Threshold, c.Parameter);
            }
            else if (entryTransition != null)
            {
                foreach (var c in parsedConditions)
                    entryTransition.AddCondition(c.Mode, c.Threshold, c.Parameter);
            }

            Persist(ctrl);
            FillIdentity(result, ctrl);
            return result;
        }

        [CliGroupCommand(Group,
            "Read an AnimatorController's full structure: parameters, layers, states (with motion / default), and transitions (with conditions).",
            MainThreadRequired = true,
            Tags = new[] { "animation/animator" })]
        public static AnimatorControllerInfo GetController(
            [CliArg("controller", "Reference to the AnimatorController to read (path / guid / globalId).", Required = true)] ObjectRef controller)
        {
            var (ctrl, ctrlPath) = ResolveController(controller);

            var info = new AnimatorControllerInfo { AssetPath = ctrlPath };

            foreach (var p in ctrl.parameters)
            {
                info.Parameters.Add(new ParameterInfo
                {
                    Name = p.name,
                    ParamType = p.type.ToString(),
                    DefaultValue = DefaultValueToken(p.type, p.defaultFloat, p.defaultInt, p.defaultBool)
                });
            }

            for (int i = 0; i < ctrl.layers.Length; i++)
            {
                var layer = ctrl.layers[i];
                var sm = layer.stateMachine;

                var layerInfo = new LayerInfo
                {
                    Name = layer.name,
                    Index = i,
                    DefaultState = sm != null && sm.defaultState != null ? sm.defaultState.name : null
                };

                if (sm != null)
                {
                    foreach (var childState in sm.states)
                    {
                        var state = childState.state;
                        layerInfo.States.Add(new StateInfo
                        {
                            Name = state.name,
                            Motion = state.motion != null ? state.motion.name : null,
                            IsDefault = sm.defaultState == state
                        });

                        foreach (var t in state.transitions)
                            layerInfo.Transitions.Add(DescribeTransition(state.name, t));
                    }

                    foreach (var t in sm.anyStateTransitions)
                        layerInfo.Transitions.Add(DescribeTransition("AnyState", t));
                }

                info.Layers.Add(layerInfo);
            }

            return info;
        }

        // ---- helpers ----

        private static TransitionInfo DescribeTransition(string from, AnimatorStateTransition t)
        {
            var to = t.isExit ? "Exit" : (t.destinationState != null ? t.destinationState.name : null);
            var ti = new TransitionInfo
            {
                From = from,
                To = to,
                HasExitTime = t.hasExitTime,
                Duration = t.duration
            };
            foreach (var c in t.conditions)
            {
                ti.Conditions.Add(new ConditionInfo
                {
                    Parameter = c.parameter,
                    Mode = c.mode.ToString(),
                    Threshold = c.threshold
                });
            }
            return ti;
        }

        private static AnimatorState FindState(AnimatorStateMachine sm, string name)
        {
            if (sm == null)
                return null;
            foreach (var childState in sm.states)
            {
                if (childState.state != null && childState.state.name == name)
                    return childState.state;
            }
            return null;
        }

        private struct ParsedCondition
        {
            public string Parameter;
            public AnimatorConditionMode Mode;
            public float Threshold;
        }

        /// <summary>
        /// Parse and validate the conditions array against the controller's parameters: each parameter
        /// must exist and its type must be compatible with the condition mode (If/IfNot for Bool/Trigger;
        /// numeric Greater/Less/Equals/NotEqual for Float/Int). Throws (writing nothing) on any mismatch.
        /// </summary>
        private static List<ParsedCondition> ParseConditions(JArray conditions, AnimatorController ctrl)
        {
            var parsed = new List<ParsedCondition>();
            if (conditions == null)
                return parsed;

            foreach (var token in conditions)
            {
                if (!(token is JObject obj))
                    throw new ArgumentException("Each condition must be an object: { parameter, mode, threshold? }.");

                var parameterName = obj["parameter"]?.ToObject<string>();
                var modeName = obj["mode"]?.ToObject<string>();
                if (string.IsNullOrWhiteSpace(parameterName))
                    throw new ArgumentException("Each condition requires a 'parameter'.");
                if (string.IsNullOrWhiteSpace(modeName))
                    throw new ArgumentException($"Condition for parameter '{parameterName}' requires a 'mode'.");

                var param = ctrl.parameters.FirstOrDefault(p => p.name == parameterName);
                if (param == null)
                    throw new ArgumentException($"Condition references parameter '{parameterName}', which does not exist on the controller.");

                if (!Enum.TryParse<AnimatorConditionMode>(modeName, ignoreCase: true, out var mode))
                    throw new ArgumentException($"Unknown condition mode '{modeName}' for parameter '{parameterName}'. Use If | IfNot | Greater | Less | Equals | NotEqual.");

                ValidateModeForType(parameterName, param.type, mode);

                var threshold = obj["threshold"]?.ToObject<float>() ?? 0f;

                parsed.Add(new ParsedCondition { Parameter = parameterName, Mode = mode, Threshold = threshold });
            }

            return parsed;
        }

        /// <summary>
        /// Enforce that a condition mode matches its parameter type: Bool/Trigger only accept If/IfNot;
        /// Float/Int only accept the numeric modes (Greater/Less/Equals/NotEqual). Unity's editor follows
        /// the same rule; we reject mismatches up front with an actionable message.
        /// </summary>
        private static void ValidateModeForType(string parameterName, AnimatorControllerParameterType type, AnimatorConditionMode mode)
        {
            var isBoolLike = type == AnimatorControllerParameterType.Bool || type == AnimatorControllerParameterType.Trigger;
            var isNumeric = type == AnimatorControllerParameterType.Float || type == AnimatorControllerParameterType.Int;

            var boolMode = mode == AnimatorConditionMode.If || mode == AnimatorConditionMode.IfNot;

            if (isBoolLike && !boolMode)
                throw new ArgumentException(
                    $"Condition mode '{mode}' is not valid for {type} parameter '{parameterName}'. Bool/Trigger parameters use If or IfNot.");

            if (isNumeric && boolMode)
                throw new ArgumentException(
                    $"Condition mode '{mode}' is not valid for {type} parameter '{parameterName}'. Float/Int parameters use Greater, Less, Equals or NotEqual.");
        }

        private static AnimatorControllerParameterType ParseParameterType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                throw new ArgumentException("type is required.");
            if (!Enum.TryParse<AnimatorControllerParameterType>(type, ignoreCase: true, out var parsed))
                throw new ArgumentException($"Unknown parameter type '{type}'. Use Float | Int | Bool | Trigger.");
            return parsed;
        }

        private static JToken DefaultValueToken(AnimatorControllerParameterType type, float f, int i, bool b)
        {
            switch (type)
            {
                case AnimatorControllerParameterType.Float: return new JValue(f);
                case AnimatorControllerParameterType.Int: return new JValue(i);
                case AnimatorControllerParameterType.Bool: return new JValue(b);
                default: return JValue.CreateNull(); // Trigger has no default value
            }
        }

        /// <summary>
        /// Resolve a layer selector (int index or string name; null/absent => 0). Returns false with an
        /// error when a name doesn't match any layer (the caller surfaces 'layer_not_found').
        /// </summary>
        private static bool TryResolveLayer(AnimatorController ctrl, JToken layer, out int index, out string error)
        {
            index = 0;
            error = null;

            if (layer == null || layer.Type == JTokenType.Null)
                return true; // default Base Layer

            if (layer.Type == JTokenType.Integer || layer.Type == JTokenType.Float)
            {
                // The layer index is an int. A float is accepted only when it is integer-valued
                // (e.g. 1.0); a fractional value like 0.9 is rejected rather than silently rounded.
                if (layer.Type == JTokenType.Float)
                {
                    var asDouble = layer.ToObject<double>();
                    if (asDouble != Math.Floor(asDouble))
                    {
                        error = $"Layer index must be an integer; got {asDouble}.";
                        return false;
                    }
                    index = (int)asDouble;
                }
                else
                {
                    index = layer.ToObject<int>();
                }

                if (index < 0 || index >= ctrl.layers.Length)
                {
                    error = $"Layer index {index} is out of range (controller has {ctrl.layers.Length} layer(s)).";
                    return false;
                }
                return true;
            }

            var name = layer.ToObject<string>();
            if (string.IsNullOrWhiteSpace(name))
                return true;

            // A numeric string is treated as an index.
            if (int.TryParse(name, out var asIndex))
            {
                if (asIndex < 0 || asIndex >= ctrl.layers.Length)
                {
                    error = $"Layer index {asIndex} is out of range (controller has {ctrl.layers.Length} layer(s)).";
                    return false;
                }
                index = asIndex;
                return true;
            }

            for (int i = 0; i < ctrl.layers.Length; i++)
            {
                if (ctrl.layers[i].name == name)
                {
                    index = i;
                    return true;
                }
            }

            error = $"No layer named '{name}' on the controller. Add it first with {CommandNames.AnimationAnimatorAddLayer}.";
            return false;
        }

        /// <summary>
        /// Reject an on-disk asset reference that resolves outside the authoring root. Mirrors the
        /// confinement applied to the controller itself so a restricted root can't be sidestepped by
        /// pointing at (e.g.) an AnimationClip/BlendTree elsewhere in the project.
        /// </summary>
        private static void ConfineToAuthoringRoot(UnityEngine.Object asset, string label, ObjectRef reference)
        {
            var assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException($"{label} reference '{reference}' does not point at an on-disk asset.");

            var confined = ProjectPaths.Resolve(assetPath, out var confineError);
            if (confined == null)
                throw new ArgumentException(
                    $"{label} '{assetPath}' is outside the authoring root '{ProjectPaths.AuthoringRoot}': {confineError}");
        }

        private static (AnimatorController controller, string path) ResolveController(ObjectRef controller)
        {
            if (controller == null || controller.IsEmpty)
                throw new ArgumentException("controller is required.");

            if (!ObjectResolver.TryResolve(controller, out var obj, out var error))
                throw new ArgumentException(error);

            if (!(obj is AnimatorController ctrl))
                throw new ArgumentException($"Reference '{controller}' resolved to a {obj.GetType().Name}, not an AnimatorController.");

            var assetPath = AssetDatabase.GetAssetPath(ctrl);
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException($"Reference '{controller}' does not point at an on-disk AnimatorController.");

            var confined = ProjectPaths.Resolve(assetPath, out var confineError);
            if (confined == null)
                throw new ArgumentException(
                    $"Controller '{assetPath}' is outside the authoring root '{ProjectPaths.AuthoringRoot}': {confineError}");

            return (ctrl, confined);
        }

        private static void Persist(AnimatorController ctrl)
        {
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
        }

        private static void FillIdentity(AuthoringResult result, AnimatorController ctrl)
        {
            var described = ObjectResolver.Describe(ctrl);
            if (described == null)
                return;
            result.Guid = described.Guid;
            result.FileId = described.FileId;
            result.GlobalId = described.GlobalId;
        }

        private static void EnsureParentFolder(string assetPath)
        {
            var parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) || AssetDatabase.IsValidFolder(parent))
                return;
            CreateFolderRecursive(parent);
        }

        private static void CreateFolderRecursive(string assetsPath)
        {
            if (AssetDatabase.IsValidFolder(assetsPath))
                return;

            var parent = Path.GetDirectoryName(assetsPath)?.Replace('\\', '/');
            var name = Path.GetFileName(assetsPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                throw new ArgumentException($"Invalid folder path '{assetsPath}'.");

            if (!AssetDatabase.IsValidFolder(parent))
                CreateFolderRecursive(parent);

            AssetDatabase.CreateFolder(parent, name);
        }
    }

    // ---- result models ----

    /// <summary>A structured, non-throwing error envelope ({ error, code }) returned at HTTP 200.</summary>
    [Serializable]
    class ErrorResult
    {
        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("code")]
        public string Code { get; set; }

        public static ErrorResult PackageStyle(string code, string error) => new ErrorResult { Code = code, Error = error };
    }

    [Serializable]
    class ParameterInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("type")]
        public string ParamType { get; set; }

        [JsonProperty("defaultValue")]
        public JToken DefaultValue { get; set; }
    }

    [Serializable]
    class AddParameterResult : AuthoringResult
    {
        [JsonProperty("parameter")]
        public ParameterInfo Parameter { get; set; }
    }

    [Serializable]
    class LayerSummary
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("index")]
        public int Index { get; set; }

        [JsonProperty("weight")]
        public float Weight { get; set; }
    }

    [Serializable]
    class AddLayerResult : AuthoringResult
    {
        [JsonProperty("layer")]
        public LayerSummary Layer { get; set; }
    }

    [Serializable]
    class StateSummary
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("layer")]
        public int Layer { get; set; }

        [JsonProperty("hasMotion")]
        public bool HasMotion { get; set; }

        [JsonProperty("isDefault")]
        public bool IsDefault { get; set; }
    }

    [Serializable]
    class AddStateResult : AuthoringResult
    {
        [JsonProperty("state")]
        public StateSummary State { get; set; }
    }

    [Serializable]
    class TransitionSummary
    {
        [JsonProperty("from")]
        public string From { get; set; }

        [JsonProperty("to")]
        public string To { get; set; }

        [JsonProperty("conditionCount")]
        public int ConditionCount { get; set; }
    }

    [Serializable]
    class AddTransitionResult : AuthoringResult
    {
        [JsonProperty("transition")]
        public TransitionSummary Transition { get; set; }
    }

    [Serializable]
    class AnimatorControllerInfo
    {
        [JsonProperty("assetPath")]
        public string AssetPath { get; set; }

        [JsonProperty("parameters")]
        public List<ParameterInfo> Parameters { get; set; } = new List<ParameterInfo>();

        [JsonProperty("layers")]
        public List<LayerInfo> Layers { get; set; } = new List<LayerInfo>();
    }

    [Serializable]
    class LayerInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("index")]
        public int Index { get; set; }

        [JsonProperty("defaultState")]
        public string DefaultState { get; set; }

        [JsonProperty("states")]
        public List<StateInfo> States { get; set; } = new List<StateInfo>();

        [JsonProperty("transitions")]
        public List<TransitionInfo> Transitions { get; set; } = new List<TransitionInfo>();
    }

    [Serializable]
    class StateInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("motion")]
        public string Motion { get; set; }

        [JsonProperty("isDefault")]
        public bool IsDefault { get; set; }
    }

    [Serializable]
    class TransitionInfo
    {
        [JsonProperty("from")]
        public string From { get; set; }

        [JsonProperty("to")]
        public string To { get; set; }

        [JsonProperty("conditions")]
        public List<ConditionInfo> Conditions { get; set; } = new List<ConditionInfo>();

        [JsonProperty("hasExitTime")]
        public bool HasExitTime { get; set; }

        [JsonProperty("duration")]
        public float Duration { get; set; }
    }

    [Serializable]
    class ConditionInfo
    {
        [JsonProperty("parameter")]
        public string Parameter { get; set; }

        [JsonProperty("mode")]
        public string Mode { get; set; }

        [JsonProperty("threshold")]
        public float Threshold { get; set; }
    }
}
