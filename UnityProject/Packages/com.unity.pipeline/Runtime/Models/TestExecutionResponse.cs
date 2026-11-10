using System;
using System.Collections.Generic;
using Unity.Pipeline.Models;

namespace Unity.Pipeline
{
    /// <summary>
    /// Response for test execution commands, containing summary and detailed results
    /// </summary>
    [Serializable]
    class TestExecutionResponse : CommandExecutionResponse
    {
        /// <summary>Aggregate pass/fail/skip counts for the run.</summary>
        public TestSummary Summary { get; set; }
        /// <summary>Per-test results.</summary>
        public List<TestResult> Results { get; set; }
        /// <summary>Total run duration, in seconds.</summary>
        public double Duration { get; set; }
        /// <summary>Path to poll for status when the run was started in async mode.</summary>
        public string StatusPath { get; set; } // For async mode
        /// <summary>Which suite was run: EditMode, PlayMode, or All.</summary>
        public string Mode { get; set; } // EditMode, PlayMode, or All
        /// <summary>The test filter that was applied, if any.</summary>
        public string FilterApplied { get; set; } // What filter was used, if any

        /// <summary>Create an empty response with initialized collections.</summary>
        public TestExecutionResponse()
        {
            Results = new List<TestResult>();
            Summary = new TestSummary();
        }
    }
}