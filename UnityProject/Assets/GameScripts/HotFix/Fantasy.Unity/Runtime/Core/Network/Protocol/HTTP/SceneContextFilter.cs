#if FANTASY_NET
using System.Threading.Tasks;
using Fantasy.Async;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Fantasy.Network.HTTP;

/// <summary>
/// 让所有实现SceneContextFilter的控制器，都在执行的Scene下执行
/// </summary>
public sealed class SceneContextFilter : IAsyncActionFilter
{
    private readonly Scene _scene;
    
    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="scene"></param>
    public SceneContextFilter(Scene scene)
    {
        _scene = scene;
    }

    /// <summary>
    /// OnActionExecutionAsync
    /// </summary>
    /// <param name="context"></param>
    /// <param name="next"></param>
    /// <returns></returns>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var tcs = FTask.Create();
        
        _scene.ThreadSynchronizationContext.Post(() =>
        {
            Action().Coroutine();
        });
        
        await tcs;
        return;

        async FTask Action()
        {
            try
            {
                await next();
            }
            finally
            {
                tcs.SetResult();
            }
        }
    }
}
#endif