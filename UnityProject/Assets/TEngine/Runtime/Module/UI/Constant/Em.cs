using System;
using System.Collections.Generic;
using System.Linq.Expressions;

public static class InstanceFactory
{
    private static readonly Dictionary<Type, Func<object>> _constructorCache = new();

    public static object CreateInstanceOptimized(Type type)
    {
        if (!_constructorCache.TryGetValue(type, out var constructor))
        {
            // 验证是否存在公共无参构造函数
            var ctor = type.GetConstructor(Type.EmptyTypes);
            if (ctor == null)
            {
                throw new MissingMethodException($"类型 {type.Name} 缺少公共无参构造函数");
            }

            // 构建表达式树：new T()
            var newExpr = Expression.New(ctor);
            var lambda = Expression.Lambda<Func<object>>(newExpr);
            constructor = lambda.Compile();

            // 缓存委托
            _constructorCache[type] = constructor;
        }

        return constructor();
    }
}
