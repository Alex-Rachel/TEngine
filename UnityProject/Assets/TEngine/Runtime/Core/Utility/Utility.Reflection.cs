using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TEngine
{
    public static partial class Utility
    {
        /// <summary>
        /// 反射相关的实用函数。
        /// </summary>
        public static class Reflection
        {
            /// <summary>
            /// 执行方法。
            /// </summary>
            /// <param name="obj">目标对象。</param>
            /// <param name="methodName">方法名。</param>
            /// <param name="parameters">方法参数。</param>
            /// <returns>返回值。</returns>
            public static object InvokeMethod(object obj, string methodName, object[] parameters = null)
            {
                if (obj == null)
                    throw new NullReferenceException($"Obj is invalid !");
                if (string.IsNullOrEmpty(methodName))
                {
                    throw new ArgumentNullException($"MethodName is invalid !");
                }

                Type type = obj.GetType();
                if (type == null)
                {
                    throw new ArgumentNullException($"Type is invalid !");
                }

                if (type.BaseType == null)
                {
                    throw new ArgumentNullException($"BaseType is invalid !");
                }

                var method = type.BaseType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
                if (method == null)
                {
                    throw new NullReferenceException($"Type : {type} can not find method : {methodName} !");
                }

                return method.Invoke(obj, parameters);
            }

            /// <summary>
            /// 执行方法.
            /// </summary>
            /// <param name="type">方法所在的type。</param>
            /// <param name="obj">目标对象。</param>
            /// <param name="methodName">方法名。</param>
            /// <param name="parameters">方法参数。</param>
            /// <returns>返回值。</returns>
            public static object InvokeMethod(Type type, object obj, string methodName, object[] parameters = null)
            {
                if (type == null)
                {
                    throw new ArgumentNullException($"Type is invalid !");
                }

                if (obj == null)
                {
                    throw new NullReferenceException($"Obj is invalid !");
                }

                if (string.IsNullOrEmpty(methodName))
                {
                    throw new ArgumentNullException($"MethodName is invalid !");
                }

                var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
                if (method == null)
                {
                    throw new NullReferenceException($"Type : {type} can not find method : {methodName} !");
                }

                return method.Invoke(obj, parameters);
            }

            /// <summary>
            /// 设置对象属性值。
            /// </summary>
            /// <param name="obj">目标对象。</param>
            /// <param name="propertyName">属性名。</param>
            /// <param name="newValue">属性值。</param>
            public static void SetPropertyValue(object obj, string propertyName, object newValue)
            {
                if (obj == null)
                {
                    throw new NullReferenceException($"Obj is invalid !");
                }

                if (string.IsNullOrEmpty(propertyName))
                {
                    throw new ArgumentNullException($"PropertyName is invalid !");
                }

                Type type = obj.GetType();
                PropertyInfo prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
                if (prop == null)
                {
                    throw new NullReferenceException($"Type : {type} can not find prop: {propertyName} !");
                }

                prop.SetValue(obj, newValue, null);
            }

            /// <summary>
            /// 设置对象属性值。
            /// 使用此方法为对象属性进行赋值时,若Type类型赋予正确,则可赋值非public类型的属性值.
            /// </summary>
            /// <param name="type">属性可写的类Type。</param>
            /// <param name="obj">目标对象。</param>
            /// <param name="propertyName">属性名。</param>
            /// <param name="value">属性值。</param>
            public static void SetPropertyValue(Type type, object obj, string propertyName, object value)
            {
                if (type == null)
                {
                    throw new ArgumentNullException($"Type is invalid !");
                }

                if (obj == null)
                {
                    throw new NullReferenceException($"Obj is invalid !");
                }

                if (string.IsNullOrEmpty(propertyName))
                {
                    throw new ArgumentNullException($"PropertyName is invalid !");
                }

                PropertyInfo prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
                if (prop == null)
                {
                    throw new NullReferenceException($"Type : {type} can not find prop: {propertyName} !");
                }

                prop.SetValue(obj, value, null);
            }

            /// <summary>
            /// 设置对象字段值。
            /// </summary>
            /// <param name="type">字段可赋值所在类Type。</param>
            /// <param name="obj">目标对象。</param>
            /// <param name="fieldName">字段名。</param>
            /// <param name="value">字段值。</param>
            public static void SetFieldValue(Type type, object obj, string fieldName, object value)
            {
                if (type == null)
                {
                    throw new ArgumentNullException($"Type is invalid !");
                }

                if (string.IsNullOrEmpty(fieldName))
                {
                    throw new ArgumentNullException($"FieldName is invalid !");
                }

                var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
                if (field == null)
                {
                    throw new NullReferenceException($"Type : {type} can not find field: {fieldName} !");
                }

                field.SetValue(obj, value);
            }

            /// <summary>
            /// 设置对象字段值。
            /// </summary>
            /// <param name="obj">目标对象。</param>
            /// <param name="fieldName">字段名。</param>
            /// <param name="value">字段值。</param>
            public static void SetFieldValue(object obj, string fieldName, object value)
            {
                if (obj == null)
                {
                    throw new NullReferenceException("Obj is invalid !");
                }

                if (string.IsNullOrEmpty(fieldName))
                {
                    throw new ArgumentNullException($"FieldName is invalid !");
                }

                Type type = obj.GetType();
                var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
                if (field == null)
                {
                    throw new NullReferenceException($"Type : {type} can not find field: {fieldName} !");
                }

                field.SetValue(obj, value);
            }

            /// <summary>
            /// 获取对象的属性值。
            /// </summary>
            /// <param name="obj">目标对象。</param>
            /// <param name="propertyName">属性名。</param>
            /// <returns>属性值。</returns>
            public static object GetPropertyValue(object obj, string propertyName)
            {
                if (obj == null)
                {
                    throw new NullReferenceException("Obj is invalid !");
                }

                if (string.IsNullOrEmpty(propertyName))
                {
                    throw new ArgumentNullException($"PropertyName is invalid !");
                }

                Type type = obj.GetType();
                var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
                if (property == null)
                {
                    throw new NullReferenceException($"Type : {type} can not find property: {propertyName} !");
                }

                return property.GetValue(obj);
            }

            /// <summary>
            /// 获取非实例对象属性。
            /// </summary>
            /// <param name="type">类型。</param>
            /// <param name="propertyName">属性名。</param>
            /// <returns>属性值。</returns>
            public static object GetNonInstancePropertyValue(Type type, string propertyName)
            {
                var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                if (property == null)
                {
                    throw new NullReferenceException($"Type : {type} can not find property: {propertyName} !");
                }

                return property.GetValue(propertyName);
            }

            /// <summary>
            /// 获取属性。
            /// </summary>
            /// <param name="type">类型。</param>
            /// <param name="propertyName">属性名。</param>
            /// <returns>属性信息</returns>
            public static PropertyInfo GetProperty(Type type, string propertyName)
            {
                var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
                if (property == null)
                {
                    throw new NullReferenceException($"Type : {type} can not find property: {propertyName} !");
                }

                return property;
            }

            /// <summary>
            /// 获取对象字段值。
            /// </summary>
            /// <param name="obj">目标对象。</param>
            /// <param name="fieldName">字段名。</param>
            /// <returns>字段值</returns>
            public static object GetFieldValue(object obj, string fieldName)
            {
                if (obj == null)
                {
                    throw new NullReferenceException("Obj is invalid !");
                }

                if (string.IsNullOrEmpty(fieldName))
                {
                    throw new ArgumentNullException($"FieldName is invalid !");
                }

                Type type = obj.GetType();
                var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
                if (field == null)
                {
                    throw new NullReferenceException($"Type : {type} can not find field: {fieldName} !");
                }

                return field.GetValue(obj);
            }

            /// <summary>
            /// 获取类Type类型中的所有字段名.
            /// </summary>
            /// <typeparam name="T">type类型。</typeparam>
            /// <returns>名称数组。</returns>
            public static string[] GetTypeAllFields<T>()
            {
                return GetTypeAllFields(typeof(T));
            }

            /// <summary>
            /// 获取类Type类型中的所有字段名.
            /// </summary>
            /// <param name="type">type类型。</param>
            /// <returns>名称数组。</returns>
            public static string[] GetTypeAllFields(Type type)
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static);
                return fields.Select(f => f.Name).ToArray();
            }

            /// <summary>
            /// 获取Type类型中所有属性字段名。
            /// </summary>
            /// <typeparam name="T">type类型。</typeparam>
            /// <returns>名称数组。</returns>
            public static string[] GetTypeAllProperties<T>()
            {
                return GetTypeAllProperties(typeof(T));
            }

            /// <summary>
            /// 获取Type类型中所有属性字段名.
            /// </summary>
            /// <param name="type">type类型。</param>
            /// <returns>名称数组。</returns>
            public static string[] GetTypeAllProperties(Type type)
            {
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static);
                return properties.Select(f => f.Name).ToArray();
            }

            /// <summary>
            /// 获取Type类型中所有字段名称与字段类型的映射。
            /// </summary>
            /// <param name="type">type类型。</param>
            /// <returns>名称与类型的映射。</returns>
            public static IDictionary<string, Type> GetTypeFieldsNameAndTypeMapping(Type type)
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static);
                return fields.ToDictionary(f => f.Name, t => t.FieldType);
            }

            /// <summary>
            /// 获取Type类型中所有属性名称与字段类型的映射。
            /// </summary>
            /// <param name="type">type类型。</param>
            /// <returns>名称与类型的映射。</returns>
            public static IDictionary<string, Type> GetTypePropertyNameAndTypeMapping(Type type)
            {
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static);
                return properties.ToDictionary(f => f.Name, t => t.PropertyType);
            }
        }
    }
}