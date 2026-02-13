using System;
using System.Collections.Concurrent;
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

namespace LightProto.Internal
{
    internal class SimpleObjectPool<T>
        where T : class
    {
        private readonly ConcurrentBag<T> _objects;
        private readonly Func<T> _objectFactory;
        private readonly Action<T>? _resetAction;
        private readonly int _maxSize;

        public SimpleObjectPool(Func<T> objectFactory, Action<T>? resetAction = null, int maxSize = 128)
        {
            _objectFactory = objectFactory ?? throw new ArgumentNullException(nameof(objectFactory));
            _resetAction = resetAction;
            _maxSize = maxSize;

            _objects = new ConcurrentBag<T>();
        }

        public T Get()
        {
            if (_objects.TryTake(out var item))
            {
                return item;
            }

            return _objectFactory();
        }

        public void Return(T item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            _resetAction?.Invoke(item);
            if (_maxSize > 0 && _objects.Count >= _maxSize)
            {
                // discard
                return;
            }
            _objects.Add(item);
        }
    }
}