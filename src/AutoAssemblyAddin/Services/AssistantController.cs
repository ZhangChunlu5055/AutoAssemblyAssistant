using System;
using System.Windows.Forms;
using AutoAssemblyAddin.UI;
using SolidWorks.Interop.sldworks;

namespace AutoAssemblyAddin.Services;

internal sealed class AssistantController : IDisposable
{
    private readonly ISldWorks _app;
    private readonly AssistantPane _view;
    private readonly SelectionContextService _selection;
    private readonly ModuleReplacementService _replacement;
    private readonly ModuleMotionService _motion;
    private readonly Action<string> _log;
    private readonly Timer _timer;
    private ReplacementPlan? _pending;
    private MotionPlan? _pendingMotion;
    private bool _busy;
    private bool _disposed;

    public AssistantController(ISldWorks app, AssistantPane view, Action<string> log)
    {
        _app = app;
        _view = view;
        _log = log;
        _selection = new SelectionContextService(app);
        _replacement = new ModuleReplacementService(app, _selection);
        _motion = new ModuleMotionService(app, _selection);
        _view.CommandSubmitted += Submit;
        _view.ReplacementAccepted += ExecuteReplacement;
        _view.ReplacementCancelled += Cancel;
        _view.MotionAccepted += ExecuteMotion;
        // WinForms timer runs on the SW UI thread. No background thread may access SW COM objects.
        // Polling also handles document close/configuration switches without retaining document event sinks.
        _timer = new Timer { Interval = 700 };
        _timer.Tick += OnTick;
        _timer.Start();
        _view.AddMessage("助手", "你好，请点击 SW 图形区模组上的面，或在特征树中选择模组，然后输入“替换该模组”。\r\n" + AssistantCommandRouter.Help);
        RefreshContext();
    }

    public void RefreshContext()
    {
        if (_disposed || _busy) return;
        try
        {
            _selection.PromoteSelection();
            _view.SetContext(_selection.Describe());
            if (_pending != null)
            {
                try { _selection.ValidateCurrent(_pending.Target); }
                catch (Exception ex)
                {
                    ClearPlan();
                    _view.AddMessage("助手", "替换计划已失效：" + ex.Message);
                }
            }
            if (_pendingMotion != null)
            {
                try { _motion.Validate(_pendingMotion); }
                catch (Exception ex)
                {
                    ClearPlan();
                    _view.AddMessage("助手", "调整计划已失效：" + ex.Message);
                }
            }
        }
        catch (Exception)
        {
            // SW may temporarily reject COM calls while a modal command is open.
            _view.SetContext("SW 正忙，暂时无法读取选择。稍后将自动刷新。");
            if (_pending != null || _pendingMotion != null)
            {
                ClearPlan();
                _view.AddMessage("助手", "无法验证原选择，待执行计划已取消。请在 SW 就绪后重新生成。");
            }
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_view.Visible) RefreshContext();
    }

    private void Submit(string text)
    {
        if (_busy || _disposed) return;
        _view.AddMessage("你", text);
        if (AssistantCommandRouter.Parse(text) == AssistantCommand.ExecuteMotion)
        {
            ExecuteMotion();
            return;
        }
        Run(() =>
        {
            switch (AssistantCommandRouter.Parse(text))
            {
                case AssistantCommand.Help:
                    _view.AddMessage("助手", AssistantCommandRouter.Help); break;
                case AssistantCommand.InspectSelection:
                    _view.AddMessage("助手", _selection.Describe()); break;
                case AssistantCommand.ReplaceModule:
                    PrepareReplacement(); break;
                case AssistantCommand.MoveModule:
                    ClearPlan();
                    MotionCommand.TryParse(text, out var motionCommand);
                    ShowMotion(_motion.Prepare(motionCommand!)); break;
                case AssistantCommand.UndoMotion:
                    ClearPlan();
                    ShowMotion(_motion.PrepareUndo()); break;
                case AssistantCommand.ExportPose:
                    _view.AddMessage("助手", "位姿已导出：\r\n" + new PoseService(_app).ExportCurrentAssembly()); break;
                case AssistantCommand.RebuildDocument:
                    ClearPlan();
                    _motion.ForgetUndo();
                    var model = _selection.GetAssembly();
                    _view.AddMessage("助手", model.EditRebuild3() ? "当前装配已重建，未自动保存。" : "重建未通过，请检查 SW 中的特征和配合错误。"); break;
                case AssistantCommand.ZoomToFit:
                    _selection.GetAssembly().ViewZoomtofit2();
                    _view.AddMessage("助手", "已缩放至整个装配体。"); break;
                case AssistantCommand.Cancel:
                    Cancel(); break;
                default:
                    _view.AddMessage("助手", "暂时无法识别这条指令，未执行操作。\r\n" + AssistantCommandRouter.Help); break;
            }
        });
    }

    private void PrepareReplacement()
    {
        ClearPlan();
        var context = _selection.Capture();
        _log($"Prepare replacement: assembly={context.AssemblyPath}; instance={context.InstanceName}; source={context.ComponentPath}; configuration={context.ComponentConfiguration}");
        using var dialog = new OpenFileDialog
        {
            Title = $"为 {context.InstanceName} 选择替换模组",
            Filter = "SolidWorks 子装配体 (*.sldasm)|*.sldasm",
            CheckFileExists = true, Multiselect = false, RestoreDirectory = true
        };
        if (dialog.ShowDialog(_view) != DialogResult.OK)
        {
            _view.AddMessage("助手", "已取消选择文件，未执行替换。");
            return;
        }
        _pending = _replacement.Prepare(context, dialog.FileName);
        _log($"Replacement candidate ready: {_pending.CandidatePath}; configurations={string.Join(",", _pending.Configurations)}");
        _view.ShowPlan($"原实例：{context.InstanceName}\r\n目标：{_pending.CandidatePath}\r\n仅此实例 · 尝试重接配合 · 不自动保存",
            _pending.Configurations, context.ComponentConfiguration);
        _view.AddMessage("助手", "替换计划已准备好。请选择目标配置，检查下方计划后点击“执行替换”。");
    }

    private void ExecuteReplacement(string configuration)
    {
        if (_busy || _disposed || _pending == null) return;
        var plan = _pending;
        _motion.ForgetUndo();
        ClearPlan(); // A failed or successful execution must never be replayed by a second click.
        _view.AddMessage("你", $"执行替换：{plan.Target.InstanceName} → {plan.CandidatePath}（{configuration}）");
        Run(() =>
        {
            _log($"Execute replacement: assembly={plan.Target.AssemblyPath}; instance={plan.Target.InstanceName}; candidate={plan.CandidatePath}; configuration={configuration}");
            var message = _replacement.Execute(plan, configuration);
            _view.AddMessage("助手", message);
            _log("Replace module: " + message);
        });
    }

    private void ShowMotion(MotionPlan plan)
    {
        _pendingMotion = plan;
        var summary = $"实例：{plan.Target.InstanceName}\r\n{plan.Description}\r\n" +
            (plan.WasFixed ? "临时浮动，完成后恢复固定。" : "保持浮动。") + "保留配合，不自动保存。";
        _view.ShowMotionPlan(summary);
        _view.AddMessage("助手", summary + "\r\n检查后点击或输入“执行调整”。若配合不允许到位或牵动其他模组，将尝试恢复原位姿。");
        _log("Motion plan: " + summary);
    }

    private void ExecuteMotion()
    {
        if (_busy || _disposed) return;
        if (_pendingMotion == null)
        {
            _view.AddMessage("助手", "没有待执行的位姿计划。请先输入如“绕 Z 轴旋转 90 度”。");
            return;
        }
        var plan = _pendingMotion;
        ClearPlan();
        Run(() =>
        {
            _log("Execute motion: " + plan.Target.InstanceName + "; " + plan.Description);
            var result = _motion.Execute(plan);
            _view.AddMessage("助手", result);
            _log(result);
        });
    }

    private void Run(Action action)
    {
        _busy = true;
        _view.SetBusy(true);
        try { action(); }
        catch (Exception ex)
        {
            _log("Assistant command: " + ex);
            _view.AddMessage("助手", "操作未完成：" + ex.Message);
        }
        finally
        {
            _busy = false;
            _view.SetBusy(false);
            RefreshContext();
        }
    }

    private void Cancel()
    {
        var existed = _pending != null || _pendingMotion != null;
        ClearPlan();
        _view.AddMessage("助手", existed ? "已取消待执行计划，装配体未改变。" : "当前没有待执行的计划。");
    }

    private void ClearPlan()
    {
        _pending = null;
        _pendingMotion = null;
        _view.HidePlan();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer.Dispose();
        _view.CommandSubmitted -= Submit;
        _view.ReplacementAccepted -= ExecuteReplacement;
        _view.ReplacementCancelled -= Cancel;
        _view.MotionAccepted -= ExecuteMotion;
        _pending = null;
        _pendingMotion = null;
        _motion.ForgetUndo();
    }
}

