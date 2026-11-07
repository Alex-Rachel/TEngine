using System;

namespace Unity.Pipeline
{
    /// <summary>
    /// Summary statistics for test execution
    /// </summary>
    [Serializable]
    class TestSummary
    {
        /// <summary>Total number of tests run.</summary>
        public int Total { get; set; }
        /// <summary>Number of tests that passed.</summary>
        public int Passed { get; set; }
        /// <summary>Number of tests that failed.</summary>
        public int Failed { get; set; }
        /// <summary>Number of tests skipped.</summary>
        public int Skipped { get; set; }
        /// <summary>Number of tests that ran but produced no pass/fail verdict.</summary>
        public int Inconclusive { get; set; }

        /// <summary>Create a zeroed summary.</summary>
        public TestSummary()
        {
            Total = 0;
            Passed = 0;
            Failed = 0;
            Skipped = 0;
            Inconclusive = 0;
        }
    }
}