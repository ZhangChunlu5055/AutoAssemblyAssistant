using System;

namespace AutoAssemblyAddin.Services;

internal enum AssistantCommand
{
    Help, InspectSelection, ReplaceModule, MoveModule, UndoMotion, ExecuteMotion, ExportPose, RebuildDocument, ZoomToFit, Cancel, Unknown
}

// Deliberately explicit: negations, multiple commands and arbitrary prose must never trigger a mutation.
// A future language/catalog adapter can return the same structured commands and replacement plans.
internal static class AssistantCommandRouter
{
    public static AssistantCommand Parse(string text)
    {
        var value = (text ?? string.Empty).Trim().TrimEnd('。', '！', '!', '？', '?').Trim();
        if (MotionCommand.TryParse(text, out _)) return AssistantCommand.MoveModule;
        switch (value.ToLowerInvariant())
        {
            case "帮助": case "help": case "你能做什么": case "移动帮助": case "旋转帮助": return AssistantCommand.Help;
            case "查看选中模组": case "查看选中组件": case "当前选择": case "选中了什么": return AssistantCommand.InspectSelection;
            case "替换该模组": case "替换模组": case "替换选中模组": case "替换该组件": return AssistantCommand.ReplaceModule;
            case "导出位姿": case "导出装配位姿": return AssistantCommand.ExportPose;
            case "重建当前装配": case "重建当前装配体": return AssistantCommand.RebuildDocument;
            case "缩放至全图": case "显示全部": return AssistantCommand.ZoomToFit;
            case "撤销上次调整": return AssistantCommand.UndoMotion;
            case "执行调整": return AssistantCommand.ExecuteMotion;
            case "取消": case "取消替换": case "取消调整": return AssistantCommand.Cancel;
            default: return AssistantCommand.Unknown;
        }
    }

    public const string Help = "可以输入：查看选中模组、替换该模组、导出位姿、重建当前装配、缩放至全图。\r\n位姿示例：沿 X 轴移动 100 毫米；沿 Y 轴移动 -20 mm；绕 Z 轴旋转 90 度；绕自身 X 轴旋转 -90 度。每次只发送一条指令。\r\n默认使用总装坐标轴，旋转中心为模组自身原点，正角度遵循右手定则。检查计划后点击或输入“执行调整”；支持“撤销上次调整”和“取消”。\r\n替换时选择本地 SLDASM，再点击“执行替换”。当前为本地指令模式，尚未接入大模型或模组推荐。";
}

