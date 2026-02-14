using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YooAsset.Editor
{
    internal class RemotePlayerSession
    {
        private readonly Queue<DebugReport> _reports = new Queue<DebugReport>();

        /// <summary>
        /// 用户ID
        /// </summary>
        public int PlayerId { private set; get; }

        /// <summary>
        /// 保存的报告最大数量
        /// </summary>
        public int MaxReportCount { private set; get; }

        public int MinRangeValue
        {
            get
            {
                return 0;
            }
        }
        public int MaxRangeValue
        {
            get
            {
                int index = _reports.Count - 1;
                if (index < 0)
                    index = 0;
                return index;
            }
        }


        public RemotePlayerSession(int playerId, int maxReportCount = 500)
        {
            PlayerId = playerId;
            MaxReportCount = maxReportCount;
        }

        /// <summary>
        /// 清理缓存数据
        /// </summary>
        public void ClearDebugReport()
        {
            _reports.Clear();
        }

        /// <summary>
        /// 添加一个调试报告
        /// </summary>
        public void AddDebugReport(DebugReport report)
        {
            if (report == null)
                Debug.LogWarning("Invalid debug report data !");

            if (_reports.Count >= MaxReportCount)
                _reports.Dequeue();
            _reports.Enqueue(report);
        }

        /// <summary>
        /// 获取调试报告
        /// </summary>
        public DebugReport GetDebugReport(int rangeIndex)
        {
            if (_reports.Count == 0)
                return null;
            if (rangeIndex < 0 || rangeIndex >= _reports.Count)
                return null;
            return _reports.ElementAt(rangeIndex);
        }

        /// <summary>
        /// 规范索引值
        /// </summary>
        public int ClampRangeIndex(int rangeIndex)
        {
            if (rangeIndex < 0)
                return 0;

            if (rangeIndex > MaxRangeValue)
                return MaxRangeValue;

            return rangeIndex;
        }
    }
}