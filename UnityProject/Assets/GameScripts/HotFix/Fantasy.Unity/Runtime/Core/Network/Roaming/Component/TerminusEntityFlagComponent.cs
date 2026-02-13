#if FANTASY_NET
using Fantasy.Entitas;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract

namespace Fantasy.Network.Roaming;

/// <summary>
/// 挂载到 Terminus 上的标记组件。
/// 用于在 Terminus 销毁时自动销毁关联的实体（autoDispose=true 时添加）。
/// 通过 LinkEntity 引用关联的实体，当 Terminus 销毁时会级联销毁该实体。
/// </summary>
internal sealed class TerminusEntityFlagComponent : Entity
{
    /// <summary>
    /// 关联的实体引用 
    /// </summary>
    public EntityReference<Entity> LinkEntity;

    public override void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        Entity linkEntity = LinkEntity;

        if (linkEntity != null)
        {
            linkEntity.Dispose();
        }

        base.Dispose();
    }
}

/// <summary>
/// 挂载到关联实体上的标记组件。
/// 用于标记该实体已被 Terminus 关联，避免重复关联到其他 Terminus。
/// 通过 Terminus 引用关联的漫游终端，当实体销毁时会级联销毁 Terminus。
/// </summary>
internal sealed class TerminusFlagComponent : Entity
{
    /// <summary>
    /// 关联的 Terminus 引用
    /// </summary>
    public EntityReference<Terminus> Terminus;

    public override void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        Terminus terminus = Terminus;

        if (terminus != null)
        {
            terminus.Dispose();
        }

        base.Dispose();
    }
}
#endif