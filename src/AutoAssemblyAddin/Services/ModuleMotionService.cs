using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace AutoAssemblyAddin.Services;

internal sealed class MotionPlan
{
    public SelectionContext Target { get; set; } = null!;
    public double[] Before { get; set; } = Array.Empty<double>();
    public double[] After { get; set; } = Array.Empty<double>();
    public bool WasFixed { get; set; }
    public bool IsUndo { get; set; }
    public string Description { get; set; } = "";
}

internal sealed class ModuleMotionService
{
    private readonly ISldWorks _app;
    private readonly SelectionContextService _selection;
    private MotionPlan? _last;
    private List<PoseSnapshot>? _lastPeers;

    public ModuleMotionService(ISldWorks app, SelectionContextService selection) { _app = app; _selection = selection; }

    public MotionPlan Prepare(MotionCommand command)
    {
        var context = _selection.Capture();
        var component = _selection.ValidateCurrent(context);
        var before = ReadPose(component);
        return new MotionPlan
        {
            Target = context, Before = before, After = MotionMath.Apply(before, command),
            WasFixed = component.IsFixed(), Description = command.Description +
                (command.Rotate ? "\r\n中心：模组自身原点；正角度遵循右手定则。" : "")
        };
    }

    public MotionPlan PrepareUndo()
    {
        if (_last == null || _lastPeers == null) throw new InvalidOperationException("没有可撤销的助手位姿调整。");
        var component = _selection.ValidateCurrent(_last.Target);
        if (!MotionMath.Equal(ReadPose(component), _last.After) || component.IsFixed() != _last.WasFixed ||
            !PeersUnchanged(_last.Target.Document, _lastPeers))
            throw new InvalidOperationException("装配状态在上次调整后已改变，无法安全撤销。请在 SW 中检查当前状态。");
        return new MotionPlan
        {
            Target = _last.Target, Before = (double[])_last.After.Clone(), After = (double[])_last.Before.Clone(),
            WasFixed = _last.WasFixed, IsUndo = true, Description = "撤销上次助手位姿调整（仅选中的同一模组）"
        };
    }

    public void ForgetUndo() { _last = null; _lastPeers = null; }

    public Component2 Validate(MotionPlan plan)
    {
        var component = _selection.ValidateCurrent(plan.Target);
        if (!MotionMath.Equal(ReadPose(component), plan.Before) || component.IsFixed() != plan.WasFixed)
            throw new InvalidOperationException("模组位置、方向或固定状态已改变，请重新输入指令。");
        if (plan.IsUndo && (_lastPeers == null || !PeersUnchanged(plan.Target.Document, _lastPeers)))
            throw new InvalidOperationException("其他模组已改变，撤销计划失效。");
        return component;
    }

    public string Execute(MotionPlan plan)
    {
        var target = Validate(plan);
        var model = plan.Target.Document;
        var assembly = (AssemblyDoc)model;
        var peers = CapturePeers(model, target);
        var utility = (MathUtility)_app.GetMathUtility();
        var transform = (MathTransform)utility.CreateTransform(plan.After.Clone());
        Select(model, target);
        var changed = false;
        try
        {
            changed = true;
            if (plan.WasFixed)
            {
                assembly.UnfixComponent();
                if (target.IsFixed()) throw new InvalidOperationException("SW 未能将目标模组临时设为浮动。");
            }
            var solved = target.SetTransformAndSolve3(transform, true);
            var rebuilt = model.EditRebuild3();
            if (!solved || !rebuilt || !MotionMath.Equal(ReadPose(target), plan.After) || !PeersUnchanged(model, peers))
                throw new InvalidOperationException("现有配合不允许精确到位、重建失败，或联动了其他模组。");
            RestoreFixedState(model, target, plan.WasFixed);
            if (!model.EditRebuild3() || !MotionMath.Equal(ReadPose(target), plan.After) || !PeersUnchanged(model, peers))
                throw new InvalidOperationException("恢复固定状态后的位姿校验未通过。");
            assembly.UpdateBox();
            model.GraphicsRedraw2();
            Select(model, target); // Fix/Unfix can clear SW selection; keep follow-up/undo commands usable.
            if (plan.IsUndo) ForgetUndo();
            else { _last = plan; _lastPeers = peers; }
            return (plan.IsUndo ? "已撤销上次位姿调整。" : "已完成模组位姿调整。") +
                $"\r\n实例：{plan.Target.InstanceName}\r\n{plan.Description}\r\n总装坐标中的原点：" +
                string.Join(", ", plan.After.Skip(9).Take(3).Select(v => (v * 1000).ToString("0.###", CultureInfo.InvariantCulture))) +
                " mm\r\n重建与位姿校验通过；其他顶层模组位姿未改变。" +
                (plan.WasFixed ? "原固定状态已恢复。" : "保持浮动状态。") + "\r\n未自动保存，未进行干涉检查。";
        }
        catch (Exception ex)
        {
            ForgetUndo();
            if (!changed) throw;
            var restored = TryRestore(model, target, plan, peers, utility);
            throw new InvalidOperationException(ex.Message + (restored
                ? "\r\n已校验恢复原位姿与固定状态。请先在 SW 中调整限制该方向的配合，再重试。"
                : "\r\n未能完整恢复，请立即检查装配状态。未自动保存。"), ex);
        }
    }

    private static bool TryRestore(ModelDoc2 model, Component2 target, MotionPlan plan, List<PoseSnapshot> peers, MathUtility utility)
    {
        try
        {
            Select(model, target);
            if (target.IsFixed()) ((AssemblyDoc)model).UnfixComponent();
            var original = (MathTransform)utility.CreateTransform(plan.Before.Clone());
            // Let the solver move a coupled floating group back together before restoring
            // individual snapshots; direct Transform2 alone can leave the solver's group pose stale.
            target.SetTransformAndSolve3(original, true);
            if (!MotionMath.Equal(ReadPose(target), plan.Before)) target.Transform2 = original;
            var restoredAll = true;
            foreach (var peer in peers)
            {
                var component = SelectionContextService.Resolve(model, peer.Reference);
                if (component == null) { restoredAll = false; continue; }
                if (peer.Pose != null && !MotionMath.Equal(ReadPose(component), peer.Pose))
                    component.Transform2 = (MathTransform)utility.CreateTransform(peer.Pose.Clone());
            }
            RestoreFixedState(model, target, plan.WasFixed);
            var rebuilt = model.EditRebuild3();
            ((AssemblyDoc)model).UpdateBox();
            model.GraphicsRedraw2();
            Select(model, target);
            return restoredAll && rebuilt && MotionMath.Equal(ReadPose(target), plan.Before) && target.IsFixed() == plan.WasFixed && PeersUnchanged(model, peers);
        }
        catch { return false; }
    }

    private static void RestoreFixedState(ModelDoc2 model, Component2 component, bool wasFixed)
    {
        Select(model, component);
        if (wasFixed && !component.IsFixed()) ((AssemblyDoc)model).FixComponent();
        if (!wasFixed && component.IsFixed()) ((AssemblyDoc)model).UnfixComponent();
        if (component.IsFixed() != wasFixed) throw new InvalidOperationException("未能恢复模组的固定状态。");
    }

    private static void Select(ModelDoc2 model, Component2 component)
    {
        model.ClearSelection2(true);
        if (!component.Select4(false, null, false)) throw new InvalidOperationException("无法选择目标模组。");
    }

    internal static double[] ReadPose(Component2 component)
    {
        var data = (component.Transform2 as MathTransform)?.ArrayData as double[];
        if (data == null) throw new InvalidOperationException("无法读取组件位姿。");
        if (data.Length != 16 || data.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
            throw new InvalidOperationException("组件位姿包含无效数据。");
        return (double[])data.Clone();
    }

    private sealed class PoseSnapshot
    {
        public byte[] Reference = Array.Empty<byte>();
        public double[]? Pose;
        public bool Fixed;
        public bool Suppressed;
        public string Path = "";
        public string Configuration = "";
    }

    private static List<PoseSnapshot> CapturePeers(ModelDoc2 model, Component2 target)
    {
        var result = new List<PoseSnapshot>();
        foreach (var component in ((object[]?)((AssemblyDoc)model).GetComponents(true) ?? Array.Empty<object>()).OfType<Component2>())
        {
            if (SelectionContextService.SameObject(component, target)) continue;
            var reference = model.Extension.GetPersistReference3(component) as byte[];
            if (reference == null || reference.Length == 0) throw new InvalidOperationException("无法记录其他组件，未执行位姿调整。");
            result.Add(new PoseSnapshot
            {
                Reference = reference, Pose = (component.GetSuppression2() == (int)SolidWorks.Interop.swconst.swComponentSuppressionState_e.swComponentSuppressed) ? null : ReadPose(component),
                Fixed = component.IsFixed(), Suppressed = (component.GetSuppression2() == (int)SolidWorks.Interop.swconst.swComponentSuppressionState_e.swComponentSuppressed),
                Path = component.GetPathName(), Configuration = component.ReferencedConfiguration
            });
        }
        return result;
    }

    private static bool PeersUnchanged(ModelDoc2 model, List<PoseSnapshot> peers)
    {
        var count = ((object[]?)((AssemblyDoc)model).GetComponents(true) ?? Array.Empty<object>()).Length;
        if (count != peers.Count + 1) return false;
        return peers.All(p =>
        {
            var component = SelectionContextService.Resolve(model, p.Reference);
            return component != null && component.IsFixed() == p.Fixed && (component.GetSuppression2() == (int)SolidWorks.Interop.swconst.swComponentSuppressionState_e.swComponentSuppressed) == p.Suppressed &&
                SelectionContextService.SamePath(component.GetPathName(), p.Path) && component.ReferencedConfiguration == p.Configuration &&
                (p.Pose == null || MotionMath.Equal(ReadPose(component), p.Pose));
        });
    }
}

