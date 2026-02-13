#if FANTASY_NET
using System.Collections.Generic;
namespace Fantasy.Assembly;

/// <summary>
/// 
/// </summary>
public interface IFantasyConfigRegistrar
{
    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    Dictionary<string, int> GetDatabaseNameDictionary();
    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    Dictionary<string, int> GetSceneTypeDictionary();
}
#endif