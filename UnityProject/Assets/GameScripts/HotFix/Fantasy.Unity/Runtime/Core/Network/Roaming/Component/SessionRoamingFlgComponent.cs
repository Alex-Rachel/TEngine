#if FANTASY_NET
using Fantasy.Async;
using Fantasy.Entitas;
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
namespace Fantasy.Network.Roaming;

internal sealed class SessionRoamingFlgComponent : Entity
{
    public bool DoNotRemove;
    public int DelayRemove;
    
    public override void Dispose()
    {
        DisposeAsync().Coroutine();
    }

    private async FTask DisposeAsync()
    {
        var roamingId = Id;

        if (!DoNotRemove && DelayRemove > 0)
        {
            var sceneRoamingComponent = Scene.RoamingComponent;

            if (sceneRoamingComponent.TryGet(roamingId, out var roamingComponent))
            {
                await roamingComponent.StopForwarding();
                await sceneRoamingComponent.Remove(roamingId, 0, DelayRemove);
            }
        }

        DelayRemove = 0;
        DoNotRemove = false;
        
        base.Dispose();
    }
}
#endif
