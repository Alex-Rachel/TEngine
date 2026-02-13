// #pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
// #pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
// namespace Fantasy
// {
//     public sealed class ScheduledTaskPool : PoolCore<ScheduledTask>
//     {
//         public ScheduledTaskPool() : base(2000) { }
//
//         public ScheduledTask Rent(Action action, ref int rounds, ref int finalSlot)
//         {
//             var scheduledTask = Rent();
//             scheduledTask.Rounds = rounds;
//             scheduledTask.Action = action;
//             scheduledTask.FinalSlot = finalSlot;
//             return scheduledTask;
//         }
//
//         public override void Return(ScheduledTask item)
//         {
//             base.Return(item);
//             item.Dispose();
//         }
//     }
//
//     public sealed class ScheduledTask : IPool, IDisposable
//     {
//         public int Rounds;
//         public int FinalSlot;
//         public Action Action;
//         public LinkedListNode<ScheduledTask> Node;
//
//         public bool IsPool { get; set; }
//         public ScheduledTask() { }
//         public ScheduledTask(Action action, ref int rounds, ref int finalSlot)
//         {
//             Action = action;
//             Rounds = rounds;
//             FinalSlot = finalSlot;
//         }
//         
//         public void Dispose()
//         {
//             Rounds = 0;
//             FinalSlot = 0;
//             Action = null;
//             Node = null;
//         }
//     }
// }