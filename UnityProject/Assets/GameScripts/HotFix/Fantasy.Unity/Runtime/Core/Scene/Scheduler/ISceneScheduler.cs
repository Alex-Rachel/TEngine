using System;

namespace Fantasy
{
    internal interface ISceneScheduler : IDisposable
    {
        void Add(Scene scene);
        void Remove(Scene scene);
        void Update();
    }
}