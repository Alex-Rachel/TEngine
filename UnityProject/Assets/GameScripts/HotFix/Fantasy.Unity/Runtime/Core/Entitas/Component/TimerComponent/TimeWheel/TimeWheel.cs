// using System.Runtime.CompilerServices;
// // ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
// #pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
//
// namespace Fantasy
// {
//     public sealed class TimeWheel
//     {
//         private int _currentIndex;
//         private ScheduledTaskPool _scheduledTaskPool;
//         
//         private readonly Scene _scene;
//         private readonly int _wheelSize;
//         private readonly int _tickDuration;
//         private readonly TimeWheel _upperLevelWheel;
//         private readonly LinkedList<ScheduledTask>[] _wheel;
//         private readonly Queue<ScheduledTask> _tasksToReschedule = new Queue<ScheduledTask>();
//         private readonly Dictionary<long, ScheduledTask> _taskDictionary = new Dictionary<long, ScheduledTask>();
//
//         public TimeWheel(TimerComponent timerComponent, int wheelSize, int tickDuration, TimeWheel upperLevelWheel = null)
//         {
//             _scene = timerComponent.Scene;
//             _wheelSize = wheelSize;
//             _tickDuration = tickDuration;
//             _upperLevelWheel = upperLevelWheel;
//             _scheduledTaskPool = timerComponent.ScheduledTaskPool;
//             _wheel = new LinkedList<ScheduledTask>[_wheelSize];
//             for (var i = 0; i < wheelSize; i++)
//             {
//                 _wheel[i] = new LinkedList<ScheduledTask>();
//             }
//         }
//
//         public long Schedule(Action action, int delay)
//         {
//             var ticks = delay / _tickDuration;
//             var futureIndex = ticks + _currentIndex;
//             var rounds = futureIndex / _wheelSize;
//             var slot = futureIndex % _wheelSize;
//
//             if (slot == 0)
//             {
//                 slot = _wheelSize - 1;
//                 rounds--;
//             }
//             else
//             {
//                 slot--;
//             }
//
//             var taskId = _scene.RuntimeIdFactory.Create;
//             var task = _scheduledTaskPool.Rent(action, ref rounds, ref slot);
//             task.Node = _wheel[slot].AddLast(task);
//             _taskDictionary.Add(taskId, task);
//             Console.WriteLine($"Schedule rounds:{rounds} slot:{slot} _currentIndex:{_currentIndex}");
//             return taskId;
//         }
//
//         public bool Remove(int taskId)
//         {
//             if (!_taskDictionary.TryGetValue(taskId, out var task))
//             {
//                 return false;
//             }
//
//             _taskDictionary.Remove(taskId);
//             _wheel[task.FinalSlot].Remove(task.Node);
//             _scheduledTaskPool.Return(task);
//             Console.WriteLine("找到已经删除了任务");
//             return true;
//         }
//
//         public void Tick(object? state)
//         {
//             var currentWheel = _wheel[_currentIndex];
//             
//             if (currentWheel.Count == 0)
//             {
//                 AdvanceIndex();
//                 return;
//             }
//
//             var currentNode = currentWheel.First;
//
//             while (currentNode != null)
//             {
//                 var nextNode = currentNode.Next;
//                 var task = currentNode.Value;
//
//                 if (task.Rounds <= 0 && task.FinalSlot == _currentIndex)
//                 {
//                     try
//                     {
//                         task.Action.Invoke();
//                     }
//                     catch (Exception ex)
//                     {
//                         Log.Error($"Exception during task execution: {ex.Message}");
//                     }
//                 }
//                 else
//                 {
//                     task.Rounds--;
//                     _tasksToReschedule.Enqueue(task);
//                 }
//
//                 currentWheel.Remove(currentNode);
//                 currentNode = nextNode;
//             }
//
//             RescheduleTasks();
//             AdvanceIndex();
//         }
//         
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         private void AdvanceIndex()
//         {
//             _currentIndex = (_currentIndex + 1) % _wheelSize;
//             if (_currentIndex == 0 && _upperLevelWheel != null)
//             {
//                 _upperLevelWheel.Tick(null);
//             }
//         }
//         
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         private void RescheduleTasks()
//         {
//             while (_tasksToReschedule.TryDequeue(out var task))
//             {
//                 _wheel[task.FinalSlot].AddLast(task);
//             }
//         }
//     }
// }