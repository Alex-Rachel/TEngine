#if FANTASY_NET
using System;
using Fantasy.Async;
using Fantasy.InnerMessage;
using Fantasy.Network.Interface;
namespace Fantasy.Network.Benchmark.Handler;

/// <summary>
/// BenchmarkRequestHandler
/// </summary>
public sealed class BenchmarkRequestHandler : MessageRPC<BenchmarkRequest, BenchmarkResponse>
{
    /// <summary>
    /// Run方法
    /// </summary>
    /// <param name="session"></param>
    /// <param name="request"></param>
    /// <param name="response"></param>
    /// <param name="reply"></param>
    protected override async FTask Run(Session session, BenchmarkRequest request, BenchmarkResponse response, Action reply)
    {
        await FTask.CompletedTask;
    }
}
#endif
