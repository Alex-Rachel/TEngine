using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LightProto.Parser
{
    public sealed class StackProtoWriter<T> : IEnumerableProtoWriter<Stack<T>, T>
    {
        public StackProtoWriter(IProtoWriter<T> itemWriter, uint tag, int itemFixedSize)
            : base(itemWriter, tag, static collection => collection.Count, itemFixedSize)
        {
        }
    }

    public sealed class StackProtoReader<T> : IEnumerableProtoReader<Stack<T>, T>
    {
        public StackProtoReader(IProtoReader<T> itemReader, uint tag, int itemFixedSize)
            : base(
                itemReader,
                static (size) => new Stack<T>(size),
                static (collection, item) =>
                {
                    collection.Push(item);
                    return collection;
                },
                itemFixedSize,
                ReverseStack
            )
        {
        }

#if NET9_0_OR_GREATER
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_array")]
    static extern ref T[] GetArray(Stack<T> stack);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_size")]
    static extern ref int GetSize(Stack<T> stack);

    static Stack<T> ReverseStack(Stack<T> stack)
    {
        var arr = GetArray(stack);
        var size = GetSize(stack);
        Array.Reverse(arr, 0, size);
        return stack;
    }
#elif NET7_0_OR_GREATER
    private static readonly Func<Stack<T>, T[]> _getArray;
    private static readonly Func<Stack<T>, int> _getSize;

    [DynamicDependency("_array", "System.Collections.Generic.Stack`1", "System.Collections")]
    [DynamicDependency("_size", "System.Collections.Generic.Stack`1", "System.Collections")]
    static StackProtoReader()
    {
        var stackType = typeof(Stack<T>);

        var arrayField = stackType.GetField(
            "_array",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var sizeField = stackType.GetField("_size", BindingFlags.NonPublic | BindingFlags.Instance);

        var param = Expression.Parameter(stackType, "stack");

        _getArray = Expression
            .Lambda<Func<Stack<T>, T[]>>(Expression.Field(param, arrayField!), param)
            .Compile();

        _getSize = Expression
            .Lambda<Func<Stack<T>, int>>(Expression.Field(param, sizeField!), param)
            .Compile();
    }

    static Stack<T> ReverseStack(Stack<T> stack)
    {
        var arr = _getArray(stack);
        var size = _getSize(stack);
        Array.Reverse(arr, 0, size);
        return stack;
    }
#else
        static Stack<T> ReverseStack(Stack<T> stack)
        {
            return new Stack<T>(stack);
        }
#endif
    }
}
