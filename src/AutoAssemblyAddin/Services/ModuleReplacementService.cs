using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AutoAssemblyAddin.Services;

internal sealed class ReplacementPlan
{
    public SelectionContext Target { get; set; } = null!;
    public string CandidatePath { get; set; } = string.Empty;
    public string[] Configurations { get; set; } = Array.Empty<string>();
    public long FileLength { get; set; }
    public DateTime FileWriteTimeUtc { get; set; }
}

internal sealed class ModuleReplacementService
{
    private readonly ISldWorks _app;
    private readonly SelectionContextService _selection;

    public ModuleReplacementService(ISldWorks app, SelectionContextService selection)
    {
        _app = app;
        _selection = selection;
    }

    public ReplacementPlan Prepare(SelectionContext context, string candidatePath)
    {
        _selection.ValidateCurrent(context);
        var file = new FileInfo(Path.GetFullPath(candidatePath));
        ValidateCandidate(context, file);
        var configurations = (_app.GetConfigurationNames(file.FullName) as object[])?
            .OfType<string>().ToArray() ?? Array.Empty<string>();
        if (configurations.Length == 0)
            throw new InvalidOperationException("无法读取候选模组配置，请检查文件是否为当前 SW 可读取的装配体。");
        return new ReplacementPlan
        {
            Target = context,
            CandidatePath = file.FullName,
            Configurations = configurations,
            FileLength = file.Length,
            FileWriteTimeUtc = file.LastWriteTimeUtc
        };
    }

    public string Execute(ReplacementPlan plan, string configuration)
    {
        var target = _selection.ValidateCurrent(plan.Target);
        var model = plan.Target.Document;
        var assembly = (AssemblyDoc)model;
        var file = new FileInfo(plan.CandidatePath);
        ValidateCandidate(plan.Target, file);
        if (file.Length != plan.FileLength || file.LastWriteTimeUtc != plan.FileWriteTimeUtc)
            throw new InvalidOperationException("候选文件在生成计划后发生变化，请重新生成计划。");
        if (!plan.Configurations.Contains(configuration, StringComparer.Ordinal))
            throw new InvalidOperationException("请选择计划中有效的候选配置。");
        EnsureComponentDocumentsSaved(model);

        // Capture every other top-level instance: identical source paths are not instance IDs.
        var siblings = new List<Tuple<byte[], string, string>>();
        foreach (var item in (object[]?)assembly.GetComponents(true) ?? Array.Empty<object>())
        {
            if (item is Component2 component && !SelectionContextService.SameObject(component, target))
            {
                var reference = model.Extension.GetPersistReference3(component) as byte[];
                if (reference == null || reference.Length == 0)
                    throw new InvalidOperationException("无法记录其他组件实例，未执行替换。");
                siblings.Add(Tuple.Create(reference, component.GetPathName(), component.ReferencedConfiguration));
            }
        }
        var originalTransform = (target.Transform2 as MathTransform)?.ArrayData as double[];
        model.ClearSelection2(true);
        if (!target.Select4(false, null, false))
            throw new InvalidOperationException("无法重新选择计划中的实例，未执行替换。");

        // From this point an exception/false return can mean a partial change. Never report rollback.
        try
        {
            var replaced = assembly.ReplaceComponents2(plan.CandidatePath, configuration, false,
                (int)swReplaceComponentsConfiguration_e.swReplaceComponentsConfiguration_ManuallySelect, true);
            if (!replaced)
                return "SW 未确认替换成功。请检查当前装配和配合状态；可能已发生部分变化。未自动保存。";

            var rebuilt = model.EditRebuild3();
            model.GraphicsRedraw2();
            var remaining = ((object[]?)assembly.GetComponents(true) ?? Array.Empty<object>())
                .OfType<Component2>().ToList();
            var otherInstancesUnchanged = true;
            foreach (var sibling in siblings)
            {
                var resolved = SelectionContextService.Resolve(model, sibling.Item1);
                if (resolved == null || !SelectionContextService.SamePath(resolved.GetPathName(), sibling.Item2) ||
                    resolved.ReferencedConfiguration != sibling.Item3)
                {
                    otherInstancesUnchanged = false;
                    continue;
                }
                remaining.RemoveAll(c => SelectionContextService.SameObject(c, resolved));
            }

            var replacement = remaining.Count == 1 ? remaining[0] : null;
            var verified = otherInstancesUnchanged && replacement != null &&
                SelectionContextService.SamePath(replacement.GetPathName(), plan.CandidatePath) &&
                replacement.ReferencedConfiguration == configuration;
            var diagnostics = ReadMateDiagnostics(model);
            var message = verified ? "已替换选中的一个模组实例。" : "SW 已执行替换，但实例校验未通过，请检查装配树。";
            message += $"\r\n原实例：{plan.Target.InstanceName}\r\n目标：{plan.CandidatePath}\r\n配置：{configuration}";
            message += rebuilt ? "\r\n重建：通过。" : "\r\n重建：未通过，请处理 SW 中的错误。";
            message += diagnostics.Count == 0 ? "\r\n顶层配合特征：未报告错误或警告。" :
                "\r\n配合待处理：" + string.Join("；", diagnostics);
            if (verified && replacement != null)
                message += DescribePose(originalTransform, (replacement.Transform2 as MathTransform)?.ArrayData as double[]);
            message += "\r\n未进行干涉或安装兼容性检查；未自动保存装配体。";
            return message;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("替换调用或后续校验异常，装配体可能已改变。请检查当前状态，未自动保存。" +
                "\r\n" + ex.Message, ex);
        }
    }

    private static void ValidateCandidate(SelectionContext context, FileInfo file)
    {
        if (!file.Exists || !file.Extension.Equals(".sldasm", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请选择存在的 SLDASM 子装配体文件。");
        if (SelectionContextService.SamePath(context.AssemblyPath, file.FullName))
            throw new InvalidOperationException("不能使用当前总装作为替换模组。");
        if (string.Equals(Path.GetFileNameWithoutExtension(context.ComponentPath),
            Path.GetFileNameWithoutExtension(file.FullName), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SW 替换接口不支持源模组与候选模组同文件名，即使它们位于不同目录。");
    }

    private void EnsureComponentDocumentsSaved(ModelDoc2 assembly)
    {
        var modified = new List<string>();
        foreach (var item in (object[]?)_app.GetDocuments() ?? Array.Empty<object>())
        {
            if (item is ModelDoc2 document && !SelectionContextService.SameObject(document, assembly) &&
                document.GetType() != (int)swDocumentTypes_e.swDocDRAWING && document.GetSaveFlag())
                modified.Add(document.GetTitle());
        }
        if (modified.Count > 0)
            throw new InvalidOperationException("SW 替换操作可能关闭组件文档。请先保存或处理以下模型的修改，再重新执行替换：\r\n" +
                string.Join("\r\n", modified.Select(name => "• " + name)) + "\r\n本次尚未调用替换，也没有自动保存这些文档。");
    }

    private static List<string> ReadMateDiagnostics(ModelDoc2 model)
    {
        var result = new List<string>();
        var feature = model.FirstFeature() as Feature;
        while (feature != null)
        {
            if (feature.GetTypeName2() == "MateGroup")
            {
                var mate = feature.GetFirstSubFeature() as Feature;
                while (mate != null)
                {
                    var code = mate.GetErrorCode2(out var warning);
                    if (code != 0) result.Add($"{mate.Name}（{(warning ? "警告" : "错误")} {code}）");
                    mate = mate.GetNextSubFeature() as Feature;
                }
            }
            feature = feature.GetNextFeature() as Feature;
        }
        return result;
    }

    private static string DescribePose(double[]? before, double[]? after)
    {
        if (before == null || after == null || before.Length != 16 || after.Length != 16)
            return "\r\n位姿：未能完整读取，需检查。";
        var distance = Math.Sqrt(Enumerable.Range(9, 3).Sum(i => Math.Pow(after[i] - before[i], 2))) * 1000;
        var rotationChanged = Enumerable.Range(0, 9).Any(i => Math.Abs(after[i] - before[i]) > 1e-8);
        return "\r\n原点位移：" + distance.ToString("0.###", CultureInfo.InvariantCulture) +
            " mm；旋转矩阵：" + (rotationChanged ? "有变化，请检查安装位置。" : "未变化。");
    }
}
