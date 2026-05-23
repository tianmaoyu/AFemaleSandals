using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading;

namespace MoppetSandal
{
    class Program
    {
        private static readonly string ToolPath = Directory.GetCurrentDirectory();
        private static readonly string CommandFile = "VirtualBoxCommand.bat";

        static void Main(string[] args)
        {
            Console.WriteLine("=== Moppet's Sandal - VirtualBox 自动化管理工具 ===\n");
            
            try
            {
                var config = new UserConfiguration();
                
                Console.WriteLine("\n=== 配置确认 ===");
                Console.WriteLine($"源虚拟机数量：{config.SourceMachines.Count}");
                Console.WriteLine($"虚拟机存储路径：{config.VirtualMachinePath}");
                Console.WriteLine($"并发启动数量：{config.ConcurrentStartCount}");
                Console.WriteLine($"循环次数：{config.CycleCount}");
                Console.WriteLine($"启动间隔（分钟）：{config.StartupInterval}");
                Console.WriteLine($"预计创建虚拟机总数：{config.CycleCount * config.ConcurrentStartCount * config.SourceMachines.Count}");
                Console.WriteLine($"预计占用硬盘空间：{config.CycleCount * config.ConcurrentStartCount * config.SourceMachines.Count * 6} GB");
                Console.WriteLine($"预计总耗时：{config.CycleCount * config.SourceMachines.Count * 3 * config.StartupInterval} 分钟\n");

                Console.Write("是否开始执行？(y/n): ");
                if (Console.ReadLine()?.ToLower() != "y")
                {
                    Console.WriteLine("操作已取消。");
                    return;
                }

                // 执行清理
                Console.WriteLine("\n=== 阶段 1: 清理旧虚拟机 ===");
                ExecuteDelete(config);

                // 执行克隆和启动循环
                Console.WriteLine("\n=== 阶段 2: 克隆和启动虚拟机 ===");
                for (int i = 0; i < config.CycleCount; i++)
                {
                    Console.WriteLine($"\n--- 循环 {i + 1}/{config.CycleCount} ---");
                    ExecuteCopyAndStart(config);
                }

                Console.WriteLine("\n=== 所有操作完成 ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n发生错误：{ex.Message}");
                Console.WriteLine($"详细信息：{ex.StackTrace}");
            }
        }

        private static void ExecuteCopyAndStart(UserConfiguration config)
        {
            var machinesToCopy = GetMachinesToCopy(config.SourceMachines);
            
            // 分批处理，每批最多 concurrentStartCount 个
            for (int i = 0; i < machinesToCopy.Count; i += config.ConcurrentStartCount)
            {
                var batch = machinesToCopy.Skip(i).Take(config.ConcurrentStartCount).ToList();
                var newMachineNames = new List<string>();

                // 写入克隆命令
                WriteCloneCommand(batch, newMachineNames);
                
                Console.WriteLine($"正在克隆 {batch.Count} 个虚拟机...");
                if (!ExecuteCommand())
                {
                    Console.WriteLine("克隆命令执行失败！");
                    continue;
                }

                // 等待克隆完成
                Thread.Sleep(TimeSpan.FromMinutes(config.StartupInterval));

                // 关闭虚拟机
                Console.WriteLine($"正在关闭 {newMachineNames.Count} 个虚拟机...");
                WritePowerOffCommand(newMachineNames);
                ExecuteCommand();
                
                Thread.Sleep(TimeSpan.FromSeconds(5));
            }
        }

        private static void ExecuteDelete(UserConfiguration config)
        {
            var machinesToDelete = GetMachinesToDelete(config.SourceMachines);
            
            if (machinesToDelete.Count == 0)
            {
                Console.WriteLine("没有需要删除的旧虚拟机。");
                return;
            }

            // 分批处理
            for (int i = 0; i < machinesToDelete.Count; i += config.ConcurrentStartCount)
            {
                var batch = machinesToDelete.Skip(i).Take(config.ConcurrentStartCount).ToList();

                // 先关闭虚拟机
                Console.WriteLine($"正在关闭 {batch.Count} 个虚拟机...");
                WritePowerOffCommand(batch);
                ExecuteCommand();
                Thread.Sleep(TimeSpan.FromSeconds(10));

                // 删除虚拟机
                Console.WriteLine($"正在删除 {batch.Count} 个虚拟机...");
                WriteDeleteCommand(batch);
                ExecuteCommand();
                
                Thread.Sleep(TimeSpan.FromSeconds(5));
            }
        }

        private static bool ExecuteCommand()
        {
            try
            {
                var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = CommandFile,
                        CreateNoWindow = false,
                        UseShellExecute = true
                    }
                };

                proc.Start();
                proc.WaitForExit();
                proc.Close();
                proc.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"命令执行失败：{ex.Message}");
                return false;
            }
        }

        private static void WriteCloneCommand(List<string> machineNames, List<string> newMachineNames)
        {
            using var sw = new StreamWriter(CommandFile, false, Encoding.UTF8);
            sw.WriteLine($"cd /d \"{ToolPath}\"");
            
            var dateStamp = DateTime.Now.ToString("yyyyMMdd");
            var random = new Random();

            foreach (var machineName in machineNames)
            {
                var newName = $"{machineName}_copy{random.Next(10000, 99999)}_{dateStamp}";
                newMachineNames.Add(newName);
                sw.WriteLine($"VBoxManage clonevm \"{machineName}\" --name \"{newName}\" --register --mode all");
            }

            // 启动新克隆的虚拟机
            foreach (var newName in newMachineNames)
            {
                sw.WriteLine($"VBoxManage startvm \"{newName}\" --type headless");
            }
        }

        private static void WriteDeleteCommand(List<string> machineNames)
        {
            using var sw = new StreamWriter(CommandFile, false, Encoding.UTF8);
            sw.WriteLine($"cd /d \"{ToolPath}\"");

            foreach (var machineName in machineNames)
            {
                sw.WriteLine($"VBoxManage unregistervm \"{machineName}\" --delete");
            }
        }

        private static void WritePowerOffCommand(List<string> machineNames)
        {
            using var sw = new StreamWriter(CommandFile, false, Encoding.UTF8);
            sw.WriteLine($"cd /d \"{ToolPath}\"");

            foreach (var machineName in machineNames)
            {
                sw.WriteLine($"VBoxManage controlvm \"{machineName}\" poweroff");
            }
        }

        private static List<string> GetMachinesToCopy(List<string> allMachines)
        {
            // 返回不包含下划线的机器名（即原始机器，非克隆版本）
            return allMachines.Where(name => !name.Contains("_")).ToList();
        }

        private static List<string> GetMachinesToDelete(List<string> allMachines)
        {
            // 获取所有带日期标记的克隆虚拟机
            var clonedMachines = allMachines.Where(name => name.Contains("_")).ToList();
            
            if (clonedMachines.Count == 0)
                return new List<string>();

            // 提取日期部分并排序
            var dates = clonedMachines
                .Select(name => name.Split('_').LastOrDefault())
                .Where(date => !string.IsNullOrEmpty(date))
                .Distinct()
                .OrderBy(date => date)
                .ToList();

            // 如果有多于 2 个不同日期的克隆，删除最早的
            if (dates.Count >= 3)
            {
                var oldestDate = dates.First();
                return clonedMachines.Where(name => name.EndsWith(oldestDate)).ToList();
            }

            return new List<string>();
        }
    }

    public class UserConfiguration
    {
        public List<string> SourceMachines { get; set; } = new();
        public string VirtualMachinePath { get; set; } = string.Empty;
        public int ConcurrentStartCount { get; set; }
        public int CycleCount { get; set; }
        public int StartupInterval { get; set; }

        public UserConfiguration()
        {
            Console.WriteLine("请按照提示输入配置信息，按回车键继续下一步。\n");
            
            SourceMachines = ReadSourceMachines();
            VirtualMachinePath = ReadVirtualMachinePath();
            ConcurrentStartCount = ReadConcurrentStartCount();
            CycleCount = ReadCycleCount();
            StartupInterval = ReadStartupInterval();
        }

        private List<string> ReadSourceMachines()
        {
            var result = new List<string>();
            Console.WriteLine("请输入要复制的源虚拟机名称（输入 'n' 结束）：");

            while (true)
            {
                Console.Write("虚拟机名称：");
                var input = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(input) || input.ToLower() == "n")
                    break;

                result.Add(input);
                Console.Write("继续添加？(y/n): ");
                if (Console.ReadLine()?.ToLower() != "y")
                    break;
            }

            if (result.Count == 0)
            {
                Console.WriteLine("至少需要指定一个源虚拟机！");
                return ReadSourceMachines();
            }

            return result;
        }

        private string ReadVirtualMachinePath()
        {
            while (true)
            {
                Console.Write("请输入 VirtualBox 虚拟机存储路径：");
                var path = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(path))
                {
                    Console.WriteLine("路径不能为空！");
                    continue;
                }

                if (!Directory.Exists(path))
                {
                    Console.WriteLine("路径不存在，请重新输入！");
                    continue;
                }

                return path;
            }
        }

        private int ReadConcurrentStartCount()
        {
            while (true)
            {
                Console.Write("请输入同时启动的虚拟机数量（建议 2-4）：");
                var input = Console.ReadLine()?.Trim();

                if (int.TryParse(input, out var count) && count >= 1 && count <= 10)
                    return count;

                Console.WriteLine("请输入 1-10 之间的数字！");
            }
        }

        private int ReadCycleCount()
        {
            while (true)
            {
                Console.Write("请输入循环次数（总数 = 循环次数 × 并发数）：");
                var input = Console.ReadLine()?.Trim();

                if (int.TryParse(input, out var count) && count >= 1 && count <= 20)
                    return count;

                Console.WriteLine("请输入 1-20 之间的数字！");
            }
        }

        private int ReadStartupInterval()
        {
            while (true)
            {
                Console.Write("请输入虚拟机启动等待时间（分钟，建议 5-15）：");
                var input = Console.ReadLine()?.Trim();

                if (int.TryParse(input, out var minutes) && minutes >= 3 && minutes <= 30)
                    return minutes;

                Console.WriteLine("请输入 3-30 之间的数字！");
            }
        }
    }
}
