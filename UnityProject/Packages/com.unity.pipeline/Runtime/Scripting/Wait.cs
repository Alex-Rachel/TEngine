using System;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.Pipeline.Scripting
{
    /// <summary>
    /// <para>Awaitable waits for work that finishes later in the same domain: a condition to become
    /// true, an <see cref="AsyncOperation"/>, an asset import. Eval scripts can use it without a
    /// using directive:</para>
    /// <code>
    /// await Wait.For(Resources.UnloadUnusedAssets());
    /// var start = Time.frameCount;
    /// await Wait.Until(() => Time.frameCount >= start + 10, because: "ten frames should pass");
    /// </code>
    /// <para>Call from the main thread. A wait polls once a frame at most, every
    /// <c>pollMs</c>, so its condition may use Unity APIs. It throws
    /// <see cref="WaitTimeoutException"/> when time runs out, and
    /// <see cref="OperationCanceledException"/> when the job running it is cancelled. A wait cannot
    /// span a domain reload.</para>
    /// </summary>
    public static class Wait
    {
        /// <summary>How long a wait on a condition lasts by default, in milliseconds.</summary>
        public const int DefaultTimeoutMs = 10000;

        /// <summary>How often a condition is checked by default, in milliseconds.</summary>
        public const int DefaultPollMs = 50;

        const int k_OperationTimeoutMs = 30000;

        /// <summary>Completes once <paramref name="condition"/> returns true.</summary>
        /// <param name="condition">Checked on the main thread.</param>
        /// <param name="timeoutMs">How long to wait before throwing <see cref="WaitTimeoutException"/>.</param>
        /// <param name="pollMs">How often to check; 0 checks every frame.</param>
        /// <param name="because">What the wait is for, added to the timeout message.</param>
        /// <returns>A task that completes when the condition holds.</returns>
        public static Task Until(Func<bool> condition, int timeoutMs = DefaultTimeoutMs, int pollMs = DefaultPollMs, string because = null)
        {
            if (condition == null)
                throw new ArgumentNullException(nameof(condition));
            return Poll(condition, timeoutMs, pollMs, () => $"Wait.Until timed out after {timeoutMs} ms{Reason(because)}");
        }

        /// <summary>
        /// Reads <paramref name="getValue"/> until <paramref name="condition"/> accepts the value, and
        /// returns that value.
        /// </summary>
        /// <param name="getValue">Read on the main thread.</param>
        /// <param name="condition">Whether a value is the one to wait for.</param>
        /// <param name="timeoutMs">How long to wait before throwing <see cref="WaitTimeoutException"/>.</param>
        /// <param name="pollMs">How often to check; 0 checks every frame.</param>
        /// <param name="because">What the wait is for, added to the timeout message.</param>
        /// <typeparam name="T">The value's type.</typeparam>
        /// <returns>The first value <paramref name="condition"/> accepted.</returns>
        public static async Task<T> Until<T>(Func<T> getValue, Func<T, bool> condition, int timeoutMs = DefaultTimeoutMs, int pollMs = DefaultPollMs, string because = null)
        {
            if (getValue == null)
                throw new ArgumentNullException(nameof(getValue));
            if (condition == null)
                throw new ArgumentNullException(nameof(condition));

            T last = default;
            await Poll(() => condition(last = getValue()), timeoutMs, pollMs,
                () => $"Wait.Until<{typeof(T).Name}> timed out after {timeoutMs} ms, last value {last}{Reason(because)}");
            return last;
        }

        /// <summary>Completes once <paramref name="operation"/> is done.</summary>
        /// <param name="operation">The operation, such as a scene load or a web request.</param>
        /// <param name="timeoutMs">How long to wait before throwing <see cref="WaitTimeoutException"/>.</param>
        /// <returns>The operation.</returns>
        public static async Task<AsyncOperation> For(AsyncOperation operation, int timeoutMs = k_OperationTimeoutMs)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            await Poll(() => operation.isDone, timeoutMs, 0,
                () => $"Wait.For(AsyncOperation) timed out after {timeoutMs} ms at progress {operation.progress:0.##}");
            return operation;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Completes once the asset at <paramref name="assetPath"/> is imported and loadable. Start the
        /// import first, for example with <c>AssetDatabase.ImportAsset</c>; this does not force one.
        /// Editor only.
        /// </summary>
        /// <param name="assetPath">The asset's project path, such as <c>Assets/Data/items.json</c>.</param>
        /// <param name="timeoutMs">How long to wait before throwing <see cref="WaitTimeoutException"/>.</param>
        /// <param name="pollMs">How often to check; 0 checks every frame.</param>
        /// <returns>A task that completes when the asset loads.</returns>
        public static Task ForImport(string assetPath, int timeoutMs = k_OperationTimeoutMs, int pollMs = DefaultPollMs)
        {
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentNullException(nameof(assetPath));

            return Poll(() => !EditorApplication.isUpdating && AssetDatabase.LoadMainAssetAtPath(assetPath) != null, timeoutMs, pollMs,
                () => $"Wait.ForImport('{assetPath}') timed out after {timeoutMs} ms; the asset never became loadable");
        }
#endif

        static string Reason(string because) => string.IsNullOrEmpty(because) ? string.Empty : $": {because}";

        static async Task Poll(Func<bool> done, int timeoutMs, int pollMs, Func<string> timeoutMessage)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + timeoutMs / 1000.0;
            var nextCheck = double.NegativeInfinity;

            while (true)
            {
                PipelineCancellation.ThrowIfCancellationRequested();

                var now = Time.realtimeSinceStartupAsDouble;
                var timedOut = now >= deadline;
                if (now >= nextCheck || timedOut)
                {
                    if (done())
                        return;
                    nextCheck = now + pollMs / 1000.0;
                }

                if (timedOut)
                    throw new WaitTimeoutException(timeoutMessage());

                await Task.Yield();
            }
        }
    }
}
