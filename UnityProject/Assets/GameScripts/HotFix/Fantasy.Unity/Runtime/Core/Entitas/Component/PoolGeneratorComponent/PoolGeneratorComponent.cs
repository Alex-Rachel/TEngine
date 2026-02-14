using System;
using System.Runtime.CompilerServices;
using Fantasy.Assembly;
using Fantasy.Async;
using Fantasy.DataStructure.Dictionary;
using Fantasy.Entitas.Interface;
using Fantasy.Pool;
// ReSharper disable CheckNamespace
// ReSharper disable ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace Fantasy.Entitas
{
    internal sealed class PoolGeneratorComponent : Entity, IAssemblyLifecycle
    {
        private RuntimeTypeHandleFrozenDictionary<Func<IPool>> _frozenDictionary;
        private readonly TypeHandleMergerFrozenDictionary<Func<IPool>> _typeHandleMerger = new();
        
        #region AssemblyManifest
        
        internal async FTask<PoolGeneratorComponent> Initialize()
        {
            await AssemblyLifecycle.Add(this);
            return this;
        }

        public async FTask OnLoad(AssemblyManifest assemblyManifest)
        {
            var tcs = FTask.Create(false);
            Scene?.ThreadSynchronizationContext.Post(() =>
            {
                var poolCreatorGenerator = assemblyManifest.PoolCreatorGenerator;
                _typeHandleMerger.Add(
                    assemblyManifest.AssemblyManifestId,
                    poolCreatorGenerator.RuntimeTypeHandles(),
                    poolCreatorGenerator.Generators());
                _frozenDictionary = _typeHandleMerger.GetFrozenDictionary();
                tcs.SetResult();
            });
            await tcs;
        }

        public async FTask OnUnload(AssemblyManifest assemblyManifest)
        {
            var tcs = FTask.Create(false);
            Scene?.ThreadSynchronizationContext.Post(() =>
            {
                if (_typeHandleMerger.Remove(assemblyManifest.AssemblyManifestId))
                {
                    _frozenDictionary = _typeHandleMerger.GetFrozenDictionary();
                }
                
                tcs.SetResult();
            });
            await tcs;
        }

        #endregion

        #region Create

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IPool Create(Type type)
        {
            if (!_frozenDictionary.TryGetValue(type.TypeHandle, out var func))
            {
                throw new InvalidOperationException(
                    $"Pool creator not found for type : {type.FullName}. Make sure the type implements IPool and is registered via Source Generator.");
            }

            return func();
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Create<T>(Type type) where T : IPool
        {
            return (T)Create(type);
        }

        #endregion
    }
}