using System.Collections.Generic;
using System.Text;

namespace AutoAssemblyAddin.Models;

public sealed class RebuildResult
{
    public string SavePath { get; set; } = string.Empty;
    public int InsertedCount { get; set; }
    public List<string> Skipped { get; set; } = new();

    public bool HasSkipped => Skipped.Count > 0;

    public string BuildMessage()
    {
        var message = new StringBuilder();
        message.AppendLine($"装配体已重建并保存到：{SavePath}");
        message.AppendLine($"成功插入 {InsertedCount} 个子装配体。");

        if (HasSkipped)
        {
            message.AppendLine($"跳过 {Skipped.Count} 个：");
            foreach (var item in Skipped)
            {
                message.AppendLine($"- {item}");
            }
        }

        return message.ToString().TrimEnd();
    }
}
