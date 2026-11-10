using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.Pipeline.Editor.Commands.Navigation
{
    /// <summary>
    /// Navigation and targeting commands (CLI-200): read and drive the Editor selection, and run
    /// Unity Search queries — both returning the canonical <see cref="AuthoringResult"/> object
    /// identity (via <see cref="ObjectResolver.Describe"/>) so an agent can reference results in a
    /// follow-up call. All commands are read-only or non-destructive (set_selection only changes the
    /// Editor selection, which carries no undo/safety policy), and run on the main thread.
    /// </summary>
    static class NavigationCommands
    {
        const string Group = "gameobject";
        const string EditorGroup = "editor";

        private const int MaxSearchResults = 200;

        [CliGroupCommand(Group, "Read the current Editor selection as structured object identities.", Tags = new[] { "navigation" })]
        public static SelectionResult GetSelection()
        {
            return DescribeSelection(null);
        }

        /// <summary>
        /// <para>Set the Editor selection from instance ids and/or asset paths.</para>
        /// <para>
        /// We accept simple <paramref name="instanceIds"/> / <paramref name="paths"/> rather than full
        /// <see cref="ObjectRef"/>[] handles because nested-object parameter schemas (arrays of objects)
        /// are pending CAT-2508. Once that lands this can take an <see cref="ObjectRef"/>[] and route
        /// each through <see cref="ObjectResolver.TryResolve"/> for the full handle surface.
        /// </para>
        /// </summary>
        [CliGroupCommand(Group, "Set the Editor selection to the given assets/scene objects.", Tags = new[] { "navigation" })]
        public static SelectionResult SetSelection(
            [CliArg("instance-ids", "Scene/loaded object instance IDs to select.")] ObjectId[] instanceIds = null,
            [CliArg("paths", "Asset paths to select (e.g. Assets/Foo.prefab).")] string[] paths = null)
        {
            var resolved = new List<Object>();
            var unresolved = new List<string>();

            if (instanceIds != null)
            {
                foreach (var id in instanceIds)
                {
                    var obj = PipelineUtils.IdToObject(id);
                    if (obj != null)
                        resolved.Add(obj);
                    else
                        unresolved.Add($"instanceId:{id}");
                }
            }

            if (paths != null)
            {
                foreach (var path in paths)
                {
                    var obj = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadMainAssetAtPath(path);
                    if (obj != null)
                        resolved.Add(obj);
                    else if (path == null)
                        unresolved.Add("<null>");
                    else if (path.Length == 0)
                        unresolved.Add("<empty>");
                    else
                        unresolved.Add(path);
                }
            }

            var hadInputs = (instanceIds != null && instanceIds.Length > 0) ||
                            (paths != null && paths.Length > 0);

            // Inputs were given but none resolved: that's a hard error, not a silent clear.
            if (resolved.Count == 0 && hadInputs)
                throw new ArgumentException($"No objects resolved from the given inputs. Unresolved: {string.Join(", ", unresolved)}");

            // No inputs at all is a valid request to clear the selection.
            var objects = resolved.ToArray();
            Selection.objects = objects;
            Selection.activeObject = resolved.FirstOrDefault();

            return DescribeSelection(unresolved.ToArray());
        }

        [CliGroupCommand(EditorGroup, "Run a Unity Search query and return structured results.", Tags = new[] { "navigation" })]
        public static SearchResult Search(
            [CliArg("query", "Unity Search query string, e.g. 't:Material', 'p: my asset', 'h: Main Camera'.", Required = true)] string query,
            [CliArg("limit", "Max results to return (capped 200).")] int limit = 50)
        {
            if (string.IsNullOrEmpty(query))
                throw new ArgumentException("query must be a non-empty Unity Search query string.");

            var clamped = Mathf.Clamp(limit, 0, MaxSearchResults);

            // A non-positive limit can't return any rows; skip the potentially expensive request.
            if (clamped <= 0)
            {
                return new SearchResult
                {
                    Query = query,
                    Count = 0,
                    Results = Array.Empty<SearchResultItem>()
                };
            }

            var results = new List<SearchResultItem>();

            ISearchList list = null;
            try
            {
                // Synchronous request returns the items already resolved on the main thread.
                list = SearchService.Request(query, SearchFlags.Synchronous);

                foreach (var item in list)
                {
                    if (results.Count >= clamped)
                        break;
                    if (item == null)
                        continue;

                    // One malformed item must not fail the whole call.
                    try
                    {
                        results.Add(MapItem(item));
                    }
                    catch
                    {
                        // skip the unmappable item
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Unity Search query '{query}' failed: {ex.Message}", ex);
            }
            finally
            {
                // The Synchronous Request overload returns an ISearchList that owns a SearchContext;
                // dispose it when present so we don't leak the context.
                (list as IDisposable)?.Dispose();
            }

            return new SearchResult
            {
                Query = query,
                Count = results.Count,
                Results = results.ToArray()
            };
        }

        private static SearchResultItem MapItem(SearchItem item)
        {
            string label;
            try
            {
                label = item.GetLabel(item.context, true);
            }
            catch
            {
                label = null;
            }

            if (string.IsNullOrEmpty(label))
                label = item.label ?? item.id;

            string description = null;
            try
            {
                description = item.GetDescription(item.context, true);
            }
            catch
            {
                // leave description null
            }

            string path = null;
            try
            {
                var obj = item.ToObject();
                if (obj != null)
                {
                    var assetPath = AssetDatabase.GetAssetPath(obj);
                    if (!string.IsNullOrEmpty(assetPath))
                        path = assetPath;
                }
            }
            catch
            {
                // leave path null
            }

            return new SearchResultItem
            {
                Id = item.id,
                Label = label,
                Description = description,
                Provider = item.provider?.id,
                Path = path
            };
        }

        /// <summary>
        /// Snapshot the current <see cref="Selection"/> as canonical identities, optionally tagging on
        /// the <paramref name="unresolved"/> list from a set_selection call.
        /// </summary>
        private static SelectionResult DescribeSelection(string[] unresolved)
        {
            var objects = Selection.objects ?? Array.Empty<Object>();
            var described = objects
                .Select(ObjectResolver.Describe)
                .Where(r => r != null)
                .ToArray();

            return new SelectionResult
            {
                Count = described.Length,
                Active = ObjectResolver.Describe(Selection.activeObject),
                Objects = described,
                Unresolved = unresolved
            };
        }
    }

    /// <summary>
    /// Structured snapshot of the Editor selection: the active object plus every selected object,
    /// each as a canonical <see cref="AuthoringResult"/> identity. <see cref="Unresolved"/> is only
    /// populated by set_selection (inputs that did not resolve to a loaded object/asset).
    /// </summary>
    [Serializable]
    class SelectionResult
    {
        /// <summary>Number of selected objects (excludes nulls).</summary>
        [JsonProperty("count")]
        public int Count { get; set; }

        /// <summary>The active selection object, or null when the selection is empty.</summary>
        [JsonProperty("active")]
        public AuthoringResult Active { get; set; }

        /// <summary>Every selected object as a canonical identity (nulls skipped).</summary>
        [JsonProperty("objects")]
        public AuthoringResult[] Objects { get; set; }

        /// <summary>Inputs that did not resolve (set_selection only); null for get_selection.</summary>
        [JsonProperty("unresolved")]
        public string[] Unresolved { get; set; }
    }

    /// <summary>
    /// Structured result of a Unity Search query: the echoed query, the returned count, and the
    /// mapped <see cref="SearchResultItem"/> rows (capped at the requested limit).
    /// </summary>
    [Serializable]
    class SearchResult
    {
        /// <summary>The query that was executed (echoed back).</summary>
        [JsonProperty("query")]
        public string Query { get; set; }

        /// <summary>Number of results returned (after the limit cap).</summary>
        [JsonProperty("count")]
        public int Count { get; set; }

        /// <summary>The mapped search result rows.</summary>
        [JsonProperty("results")]
        public SearchResultItem[] Results { get; set; }
    }

    /// <summary>
    /// One Unity Search result, flattened to portable fields. <see cref="Path"/> is populated only
    /// when the item resolves to an asset object.
    /// </summary>
    [Serializable]
    class SearchResultItem
    {
        /// <summary>Provider-specific item id (e.g. an asset path or scene object id).</summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>Display label for the item.</summary>
        [JsonProperty("label")]
        public string Label { get; set; }

        /// <summary>Longer description, when the provider supplies one (may be null).</summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>Id of the search provider that produced this item (e.g. "asset", "scene").</summary>
        [JsonProperty("provider")]
        public string Provider { get; set; }

        /// <summary>Project-relative asset path, when the item resolves to an asset (else null).</summary>
        [JsonProperty("path")]
        public string Path { get; set; }
    }
}
