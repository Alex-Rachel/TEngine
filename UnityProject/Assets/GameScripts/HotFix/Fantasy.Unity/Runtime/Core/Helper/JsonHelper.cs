using System;
using Newtonsoft.Json;
#pragma warning disable CS8603

namespace Fantasy.Helper
{
    /// <summary>
    /// 提供操作 JSON 数据的辅助方法。
    /// </summary>
    public static partial class JsonHelper
    {
        /// <summary>
        /// 将对象序列化为 JSON 字符串。
        /// </summary>
        /// <typeparam name="T">要序列化的对象类型。</typeparam>
        /// <param name="t">要序列化的对象。</param>
        /// <returns>表示序列化对象的 JSON 字符串。</returns>
        public static string ToJson<T>(this T t)
        {
            return JsonConvert.SerializeObject(t);
        }

        /// <summary>
        /// 反序列化 JSON 字符串为指定类型的对象。
        /// </summary>
        /// <param name="json">要反序列化的 JSON 字符串。</param>
        /// <param name="type">目标对象的类型。</param>
        /// <param name="reflection">是否使用反射进行反序列化（默认为 true）。</param>
        /// <returns>反序列化后的对象。</returns>
        public static object Deserialize(this string json, Type type, bool reflection = true)
        {
            return JsonConvert.DeserializeObject(json, type);
        }

        /// <summary>
        /// 反序列化 JSON 字符串为指定类型的对象。
        /// </summary>
        /// <typeparam name="T">目标对象的类型。</typeparam>
        /// <param name="json">要反序列化的 JSON 字符串。</param>
        /// <returns>反序列化后的对象。</returns>
        public static T Deserialize<T>(this string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }

        /// <summary>
        /// 克隆对象，通过将对象序列化为 JSON，然后再进行反序列化。
        /// </summary>
        /// <typeparam name="T">要克隆的对象类型。</typeparam>
        /// <param name="t">要克隆的对象。</param>
        /// <returns>克隆后的对象。</returns>
        public static T Clone<T>(T t)
        {
            return t.ToJson().Deserialize<T>();
        }
    }
}