using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Testing;
using Unity.Pipeline.Models;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands
{
    /// <summary>
    /// Commands for running Unity tests programmatically
    /// </summary>
    static class TestCommands
    {
        const string Group = "test";

        /// <summary>
        /// Execute Unity tests with filtering options.
        /// Synchronous by default (blocks until completion), async mode available with --async-tests flag.
        /// </summary>
        [CliGroupCommand(Group, "Execute Unity tests with filtering options", MainThreadRequired = true, Tags = new[] { "tests" })]
        public static async Task<TestExecutionResponse> Run(
            [CliArg("mode", "Test mode: all, editor, playmode (default: all)")] string mode = "all",
            [CliArg("filter", "Test name filter pattern (case-insensitive partial match)")] string filter = "",
            [CliArg("filter-type", "Filter type: testName, assembly, category (default: testName)")] string filterType = "testName",
            [CliArg("include-explicit", "Include tests marked with [Explicit] attribute")] bool includeExplicit = false,
            [CliArg("async-tests", "Run asynchronously - return immediately, poll /test-status for results")] bool asyncTests = false,
            [CliArg("timeout", "Test execution timeout in seconds (default: 600)")] double timeoutInSeconds = 600)
        {
            // Return the full structured response (including failures and the Error field on a
            // failed run) rather than throwing: the server now awaits this Task and serializes the
            // unwrapped response, so the client receives complete, structured reporting. Throwing
            // would only surface an opaque message and discard the per-test results.
            return await PipelineTestRunner.ExecuteTestsAsync(
                mode,
                filter,
                filterType,
                includeExplicit,
                asyncTests,
                PipelineTimeout.FromSeconds(timeoutInSeconds));
        }

        /// <summary>
        /// List all available tests without executing any of them.
        /// Enumerates the test tree via TestRunnerApi.RetrieveTestList for the requested mode(s).
        /// </summary>
        [CliGroupCommand(Group, "List all available tests (EditMode and/or PlayMode) without running them", MainThreadRequired = true, Tags = new[] { "tests" })]
        public static async Task<TestListResponse> List(
            [CliArg("mode", "Test mode: all, editor, playmode (default: all)")] string mode = "all")
        {
            try
            {
                if (!TryGetModes(mode, out var modes))
                {
                    return new TestListResponse
                    {
                        Success = false,
                        Command = CommandNames.TestList,
                        Error = $"Invalid mode '{mode}'. Use 'editor', 'playmode', or 'all'.",
                        ExecutedAt = DateTime.UtcNow
                    };
                }

                var tests = new List<TestListItem>();
                foreach (var testMode in modes)
                {
                    tests.AddRange(await RetrieveTestsAsync(testMode));
                }

                return new TestListResponse
                {
                    Success = true,
                    Command = CommandNames.TestList,
                    Mode = modes.Count == 2 ? "All" : modes[0].ToString(),
                    Count = tests.Count,
                    Tests = tests,
                    Message = $"Found {tests.Count} test(s).",
                    ExecutedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TestCommands] {CommandNames.TestList} failed: {ex.Message}");
                return new TestListResponse
                {
                    Success = false,
                    Command = CommandNames.TestList,
                    Error = "Failed to list tests",
                    ErrorDetails = ex.ToString(),
                    ExecutedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Get current test status for async test execution
        /// </summary>
        [CliGroupCommand(Group, "Get status of running async test execution", MainThreadRequired = false, Tags = new[] { "tests" })]
        [ImmediateCommand]
        public static JToken Status()
        {
            var status = PipelineTestRunner.GetTestStatus();
            return status != null
                ? StatusFileReader.Parse(status)
                : new JObject { ["status"] = "no_tests", ["message"] = "No test run in progress" };
        }

        /// <summary>
        /// Cancel running test execution
        /// </summary>
        [CliGroupCommand(Group, "Cancel running test execution", MainThreadRequired = true, Tags = new[] { "tests" })]
        public static object Cancel()
        {
            return PipelineTestRunner.CancelTests();
        }

        /// <summary>
        /// Resolve a mode string to the concrete TestMode(s) to enumerate.
        /// </summary>
        private static bool TryGetModes(string mode, out List<TestMode> modes)
        {
            switch (mode?.ToLowerInvariant())
            {
                case null:
                case "":
                case "all":
                    modes = new List<TestMode> { TestMode.EditMode, TestMode.PlayMode };
                    return true;
                case "editor":
                case "editmode":
                    modes = new List<TestMode> { TestMode.EditMode };
                    return true;
                case "playmode":
                case "play":
                    modes = new List<TestMode> { TestMode.PlayMode };
                    return true;
                default:
                    modes = null;
                    return false;
            }
        }

        /// <summary>
        /// Retrieve the test list for a single mode. RetrieveTestList enumerates the test tree (no
        /// execution) and invokes the callback on the main thread; we bridge it to a Task.
        /// </summary>
        private static Task<List<TestListItem>> RetrieveTestsAsync(TestMode testMode)
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var tcs = new TaskCompletionSource<List<TestListItem>>();

            api.RetrieveTestList(testMode, root =>
            {
                try
                {
                    var items = new List<TestListItem>();
                    CollectLeafTests(root, testMode, items);
                    tcs.SetResult(items);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return tcs.Task;
        }

        /// <summary>
        /// Walk the test tree and collect leaf tests (actual test methods, not suites/fixtures).
        /// </summary>
        private static void CollectLeafTests(ITestAdaptor test, TestMode testMode, List<TestListItem> items)
        {
            if (test == null)
                return;

            if (!test.HasChildren && !test.IsSuite)
            {
                items.Add(new TestListItem
                {
                    FullName = test.FullName,
                    Mode = testMode.ToString(),
                    Assembly = test.TypeInfo?.Assembly?.GetName()?.Name,
                    Categories = test.Categories?.ToList() ?? new List<string>(),
                    Explicit = test.RunState == RunState.Explicit
                });
                return;
            }

            if (test.Children != null)
            {
                foreach (var child in test.Children)
                    CollectLeafTests(child, testMode, items);
            }
        }
    }

    /// <summary>
    /// Response for the list_tests command: the available tests, without execution results.
    /// </summary>
    [Serializable]
    class TestListResponse : CommandExecutionResponse
    {
        public string Mode { get; set; }       // EditMode, PlayMode, or All
        public int Count { get; set; }
        public List<TestListItem> Tests { get; set; } = new List<TestListItem>();
    }

    /// <summary>
    /// A single available test (no run state / outcome — this is a listing, not a result).
    /// </summary>
    [Serializable]
    class TestListItem
    {
        public string FullName { get; set; }
        public string Mode { get; set; }
        public string Assembly { get; set; }
        public List<string> Categories { get; set; } = new List<string>();
        public bool Explicit { get; set; }
    }
}
