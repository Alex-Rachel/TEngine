#if UNITY_2019_4_OR_NEWER
using System;
using UnityEditor;
using UnityEngine;

namespace YooAsset.Editor
{
    /// <summary>
    /// 指定标题的比较搜索
    /// </summary>
    public class SearchCompare : ISearchCommand
    {
        public string HeaderTitle;
        public string CompareValue;
        public string CompareOperator;

        /// <summary>
        /// 转换的整形数值
        /// </summary>
        private bool _isConvertedIntegerNum = false;
        private long _convertedIntegerNumber = 0;
        public long IntegerNumberValue
        {
            get
            {
                if (_isConvertedIntegerNum == false)
                {
                    _isConvertedIntegerNum = true;
                    _convertedIntegerNumber = long.Parse(CompareValue.ToString());
                }
                return _convertedIntegerNumber;
            }
        }

        /// <summary>
        /// 转换的浮点数值
        /// </summary>
        private bool _isConvertedSingleNum = false;
        private double _convertedSingleNumber = 0;
        public double SingleNumberValue
        {
            get
            {
                if (_isConvertedSingleNum == false)
                {
                    _isConvertedSingleNum = true;
                    _convertedSingleNumber = double.Parse(CompareValue.ToString());
                }
                return _convertedSingleNumber;
            }
        }

        public bool CompareTo(long value)
        {
            if (CompareOperator == ">")
            {
                if (value > SingleNumberValue)
                    return true;
            }
            else if (CompareOperator == ">=")
            {
                if (value >= SingleNumberValue)
                    return true;
            }
            else if (CompareOperator == "<")
            {
                if (value < SingleNumberValue)
                    return true;
            }
            else if (CompareOperator == "<=")
            {
                if (value <= SingleNumberValue)
                    return true;
            }
            else if (CompareOperator == "=")
            {
                if (value == SingleNumberValue)
                    return true;
            }
            else if (CompareOperator == "!=")
            {
                if (value != SingleNumberValue)
                    return true;
            }
            else
            {
                Debug.LogWarning($"Can not support operator : {CompareOperator}");
            }

            return false;
        }
        public bool CompareTo(double value)
        {
            if (CompareOperator == ">")
            {
                if (value > IntegerNumberValue)
                    return true;
            }
            else if (CompareOperator == ">=")
            {
                if (value >= IntegerNumberValue)
                    return true;
            }
            else if (CompareOperator == "<")
            {
                if (value < IntegerNumberValue)
                    return true;
            }
            else if (CompareOperator == "<=")
            {
                if (value <= IntegerNumberValue)
                    return true;
            }
            else if (CompareOperator == "=")
            {
                if (value == IntegerNumberValue)
                    return true;
            }
            else if (CompareOperator == "!=")
            {
                if (value != IntegerNumberValue)
                    return true;
            }
            else
            {
                Debug.LogWarning($"Can not support operator : {CompareOperator}");
            }

            return false;
        }
    }
}
#endif