using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Fantasy.Helper
{
    /// <summary>
    /// 文件操作助手类，提供了各种文件操作方法。
    /// </summary>
    public static partial class FileHelper
    {
        /// <summary>
        /// 获取相对路径的完整路径。
        /// </summary>
        /// <param name="relativePath">相对路径。</param>
        /// <returns>完整路径。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetFullPath(string relativePath)
        {
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativePath));
        }

        /// <summary>
        /// 获取相对路径的完整路径。
        /// </summary>
        /// <param name="relativePath">相对于指定的目录的相对路径。</param>
        /// <param name="srcDir">指定的目录</param>
        /// <returns>完整路径。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetFullPath(string relativePath, string srcDir)
        {
            return Path.GetFullPath(Path.Combine(srcDir, relativePath));
        }

        /// <summary>
        /// 获取相对路径的的文本信息。
        /// </summary>
        /// <param name="relativePath"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static async Task<string> GetTextByRelativePath(string relativePath)
        {
            var fullPath = GetFullPath(relativePath);
            return await File.ReadAllTextAsync(fullPath, Encoding.UTF8);
        }
        
        /// <summary>
        /// 获取绝对路径的的文本信息。
        /// </summary>
        /// <param name="fullPath"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static async Task<string> GetText(string fullPath)
        {
            return await File.ReadAllTextAsync(fullPath, Encoding.UTF8);
        }

        /// <summary>
        /// 根据文件夹路径创建文件夹，如果文件夹不存在会自动创建文件夹。
        /// </summary>
        /// <param name="directoryPath"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CreateDirectory(string directoryPath)
        {
            if (directoryPath.LastIndexOf('/') != directoryPath.Length - 1)
            {
                directoryPath += "/";
            }
            
            var directoriesByFilePath = GetDirectoriesByFilePath(directoryPath);
            
            foreach (var dir in directoriesByFilePath)
            {
                if (Directory.Exists(dir))
                {
                    continue;
                }

                Directory.CreateDirectory(dir);
            }
        }

        /// <summary>
        /// 将文件复制到目标路径，如果目标目录不存在会自动创建目录。
        /// </summary>
        /// <param name="sourceFile">源文件路径。</param>
        /// <param name="destinationFile">目标文件路径。</param>
        /// <param name="overwrite">是否覆盖已存在的目标文件。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Copy(string sourceFile, string destinationFile, bool overwrite)
        {
            var directory = Path.GetDirectoryName(destinationFile);
            
            if (!string.IsNullOrEmpty(directory))
            {
                CreateDirectory(directory + "/");
            }
            
            File.Copy(sourceFile, destinationFile, overwrite);
        }

        /// <summary>
        /// 获取文件路径内的所有文件夹路径。
        /// </summary>
        /// <param name="filePath">文件路径。</param>
        /// <returns>文件夹路径列表。</returns>
        public static IEnumerable<string> GetDirectoriesByFilePath(string filePath)
        {
            var dir = "";
            var fileDirectories = filePath.Split('/');

            for (var i = 0; i < fileDirectories.Length - 1; i++)
            {
                dir = $"{dir}{fileDirectories[i]}/";
                yield return dir;
            }

            if (fileDirectories.Length == 1)
            {
                yield return filePath;
            }
        }

        /// <summary>
        /// 将文件夹内的所有内容复制到目标位置。
        /// </summary>
        /// <param name="sourceDirectory">源文件夹路径。</param>
        /// <param name="destinationDirectory">目标文件夹路径。</param>
        /// <param name="overwrite">是否覆盖已存在的文件。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyDirectory(string sourceDirectory, string destinationDirectory, bool overwrite)
        {
            // 创建目标文件夹

            if (!Directory.Exists(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            // 获取当前文件夹中的所有文件

            var files = Directory.GetFiles(sourceDirectory);

            // 拷贝文件到目标文件夹

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var destinationPath = Path.Combine(destinationDirectory, fileName);
                File.Copy(file, destinationPath, overwrite);
            }

            // 获取源文件夹中的所有子文件夹

            var directories = Directory.GetDirectories(sourceDirectory);

            // 递归方式拷贝文件夹

            foreach (var directory in directories)
            {
                var directoryName = Path.GetFileName(directory);
                var destinationPath = Path.Combine(destinationDirectory, directoryName);
                CopyDirectory(directory, destinationPath, overwrite);
            }
        }

        /// <summary>
        /// 获取目录下的所有文件
        /// </summary>
        /// <param name="folderPath">文件夹路径。</param>
        /// <param name="searchPattern">需要查找的文件通配符</param>
        /// <param name="searchOption">查找的类型</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string[] GetDirectoryFile(string folderPath, string searchPattern, SearchOption searchOption)
        {
            return Directory.GetFiles(folderPath, searchPattern, searchOption);
        }

        /// <summary>
        /// 清空文件夹内的所有文件。
        /// </summary>
        /// <param name="folderPath">文件夹路径。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ClearDirectoryFile(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                return;
            }
        
            var files = Directory.GetFiles(folderPath);
        
            foreach (var file in files)
            {
                File.Delete(file);
            }
        }
    }
}