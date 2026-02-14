using System;
using Fantasy.DataStructure.Collection;

namespace Fantasy.Assembly
{
    /// <summary>
    /// 
    /// </summary>
    public interface ICustomInterface
    {
    
    }

    /// <summary>
    /// 
    /// </summary>
    public interface ICustomInterfaceRegistrar
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="customRegistrar"></param>
        void Register(OneToManyList<RuntimeTypeHandle, Type> customRegistrar);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="customRegistrar"></param>
        void UnRegister(OneToManyList<RuntimeTypeHandle, Type> customRegistrar);
    }
}