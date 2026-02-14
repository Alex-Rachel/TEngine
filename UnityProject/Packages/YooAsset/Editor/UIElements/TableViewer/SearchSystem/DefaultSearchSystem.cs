#if UNITY_2019_4_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace YooAsset.Editor
{
    public static class DefaultSearchSystem
    {
        /// <summary>
        /// 解析搜索命令
        /// </summary>
        public static List<ISearchCommand> ParseCommand(string commandContent)
        {
            if (string.IsNullOrEmpty(commandContent))
                return new List<ISearchCommand>();

            List<ISearchCommand> results = new List<ISearchCommand>(10);
            string[] commands = Regex.Split(commandContent, @"\s+");
            foreach (var command in commands)
            {
                if (command.Contains("!="))
                {
                    var splits = command.Split(new string[] { "!=" }, StringSplitOptions.None);
                    if (CheckSplitsValid(command, splits) == false)
                        continue;

                    var cmd = new SearchCompare();
                    cmd.HeaderTitle = splits[0];
                    cmd.CompareValue = splits[1];
                    cmd.CompareOperator = "!=";
                    results.Add(cmd);
                }
                else if (command.Contains(">="))
                {
                    var splits = command.Split(new string[] { ">=" }, StringSplitOptions.None);
                    if (CheckSplitsValid(command, splits) == false)
                        continue;

                    var cmd = new SearchCompare();
                    cmd.HeaderTitle = splits[0];
                    cmd.CompareValue = splits[1];
                    cmd.CompareOperator = ">=";
                    results.Add(cmd);
                }
                else if (command.Contains("<="))
                {
                    var splits = command.Split(new string[] { "<=" }, StringSplitOptions.None);
                    if (CheckSplitsValid(command, splits) == false)
                        continue;

                    var cmd = new SearchCompare();
                    cmd.HeaderTitle = splits[0];
                    cmd.CompareValue = splits[1];
                    cmd.CompareOperator = "<=";
                    results.Add(cmd);
                }
                else if (command.Contains(">"))
                {
                    var splits = command.Split('>');
                    if (CheckSplitsValid(command, splits) == false)
                        continue;

                    var cmd = new SearchCompare();
                    cmd.HeaderTitle = splits[0];
                    cmd.CompareValue = splits[1];
                    cmd.CompareOperator = ">";
                    results.Add(cmd);
                }
                else if (command.Contains("<"))
                {
                    var splits = command.Split('<');
                    if (CheckSplitsValid(command, splits) == false)
                        continue;

                    var cmd = new SearchCompare();
                    cmd.HeaderTitle = splits[0];
                    cmd.CompareValue = splits[1];
                    cmd.CompareOperator = "<";
                    results.Add(cmd);
                }
                else if (command.Contains("="))
                {
                    var splits = command.Split('=');
                    if (CheckSplitsValid(command, splits) == false)
                        continue;

                    var cmd = new SearchCompare();
                    cmd.HeaderTitle = splits[0];
                    cmd.CompareValue = splits[1];
                    cmd.CompareOperator = "=";
                    results.Add(cmd);
                }
                else if (command.Contains(":"))
                {
                    var splits = command.Split(':');
                    if (CheckSplitsValid(command, splits) == false)
                        continue;

                    var cmd = new SearchKeyword();
                    cmd.SearchTag = splits[0];
                    cmd.Keyword = splits[1];
                    results.Add(cmd);
                }
                else
                {
                    var cmd = new SearchKeyword();
                    cmd.SearchTag = string.Empty;
                    cmd.Keyword = command;
                    results.Add(cmd);
                }
            }
            return results;
        }
        private static bool CheckSplitsValid(string command, string[] splits)
        {
            if (splits.Length != 2)
            {
                Debug.LogWarning($"Invalid search command : {command}");
                return false;
            }

            if (string.IsNullOrEmpty(splits[0]))
                return false;
            if (string.IsNullOrEmpty(splits[1]))
                return false;

            return true;
        }

        /// <summary>
        /// 搜索匹配
        /// </summary>
        public static void Search(List<ITableData> sourceDatas, string command)
        {
            var searchCmds = ParseCommand(command);
            foreach (var tableData in sourceDatas)
            {
                tableData.Visible = Search(tableData, searchCmds);
            }
        }
        private static bool Search(ITableData tableData, List<ISearchCommand> commands)
        {
            if (commands.Count == 0)
                return true;

            // 先匹配字符串
            var searchKeywordCmds = commands.Where(cmd => cmd is SearchKeyword).ToList();
            if (SearchKeyword(tableData, searchKeywordCmds) == false)
                return false;

            // 后匹配数值
            var searchCompareCmds = commands.Where(cmd => cmd is SearchCompare).ToList();
            if (SearchCompare(tableData, searchCompareCmds) == false)
                return false;

            return true;
        }
        private static bool SearchKeyword(ITableData tableData, List<ISearchCommand> commands)
        {
            if (commands.Count == 0)
                return true;

            // 匹配规则：任意字符串列匹配成果
            foreach (var tableCell in tableData.Cells)
            {
                foreach (var cmd in commands)
                {
                    var searchKeywordCmd = cmd as SearchKeyword;
                    if (tableCell is StringValueCell stringValueCell)
                    {
                        if (string.IsNullOrEmpty(searchKeywordCmd.SearchTag) == false)
                        {
                            if (searchKeywordCmd.SearchTag == stringValueCell.SearchTag)
                            {
                                if (searchKeywordCmd.CompareTo(stringValueCell.StringValue))
                                    return true;
                            }
                        }
                        else
                        {
                            if (searchKeywordCmd.CompareTo(stringValueCell.StringValue))
                                return true;
                        }
                    }
                }
            }

            // 匹配失败
            return false;
        }
        private static bool SearchCompare(ITableData tableData, List<ISearchCommand> commands)
        {
            if (commands.Count == 0)
                return true;

            // 匹配规则：任意指定数值列匹配成果
            foreach (var tableCell in tableData.Cells)
            {
                foreach (var cmd in commands)
                {
                    var searchCompareCmd = cmd as SearchCompare;
                    if (tableCell is IntegerValueCell integerValueCell)
                    {
                        if (searchCompareCmd.HeaderTitle == integerValueCell.SearchTag)
                        {
                            if (searchCompareCmd.CompareTo(integerValueCell.IntegerValue))
                                return true;
                        }
                    }
                    else if (tableCell is SingleValueCell singleValueCell)
                    {
                        if (searchCompareCmd.HeaderTitle == singleValueCell.SearchTag)
                        {
                            if (searchCompareCmd.CompareTo(singleValueCell.SingleValue))
                                return true;
                        }
                    }
                }
            }

            // 匹配失败
            return false;
        }
    }
}
#endif