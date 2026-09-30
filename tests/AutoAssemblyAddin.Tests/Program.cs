using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AutoAssemblyAddin.Services;
using AutoAssemblyAddin.UI;
using SolidWorks.Interop.sldworks;

internal static class Program
{
    private static int _checks;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--replacement-integration")
                return ReplacementIntegration.Run(Path.GetFullPath(args[1]));
            if (args.Length > 0 && args[0] == "--motion-integration")
                return ReplacementIntegration.Run(Path.GetFullPath(args[1]), true);
            if (args.Length > 0 && args[0] == "--live-inspect")
            {
                var app = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
                Console.WriteLine("READ ONLY: " + new SelectionContextService(app).Describe());
                var model = app.ActiveDoc as ModelDoc2;
                if (model is AssemblyDoc assembly)
                {
                    var editedComponent = assembly.GetEditTargetComponent();
                    var editedModel = assembly.GetEditTarget() as ModelDoc2;
                    Console.WriteLine($"Edit component: {editedComponent?.Name2}; isRoot={editedComponent?.IsRoot()}");
                    Console.WriteLine($"Edit document: {editedModel?.GetTitle()}; activeDocumentSame={editedModel != null && SelectionContextService.SameObject(model, editedModel)}");
                    Console.WriteLine("Dirty documents: " + string.Join(", ", ((object[]?)app.GetDocuments() ?? Array.Empty<object>())
                        .OfType<ModelDoc2>().Where(d => d.GetSaveFlag()).Select(d => d.GetTitle())));
                    try
                    {
                        foreach (var component in ((object[])assembly.GetComponents(true)).OfType<Component2>())
                            Console.WriteLine($"STATE {component.Name2}: suppression={component.GetSuppression2()}, IsSuppressed={component.IsSuppressed()}, virtual={component.IsVirtual}, loaded={component.GetModelDoc2() != null}");
                    }
                    catch (Exception ex) { Console.WriteLine("Selection validation: " + ex.Message); }
                }
                return 0;
            }
            foreach (var text in new[] { "替换该模组", " 替换该模组。 ", "替换选中模组", "替换该组件" })
                Check(AssistantCommandRouter.Parse(text) == AssistantCommand.ReplaceModule, text);
            // Never execute a destructive verb merely because it appears somewhere in prose.
            foreach (var text in new[] { "不要替换该模组", "不替换", "取消替换该模组", "如何替换该模组", "先替换该模组再导出位姿", "替换成视觉模组", "替换该模组\n导出位姿", "删除该模组", "", "   " })
                Check(AssistantCommandRouter.Parse(text) == AssistantCommand.Unknown, "Unsafe/unknown: " + text);
            Check(AssistantCommandRouter.Parse("取消替换") == AssistantCommand.Cancel, "Cancel");
            Check(AssistantCommandRouter.Parse("查看选中模组") == AssistantCommand.InspectSelection, "Inspect");
            Check(AssistantCommandRouter.Parse("重建当前装配") == AssistantCommand.RebuildDocument, "Rebuild");
            Check(AssistantCommandRouter.Parse("导出位姿") == AssistantCommand.ExportPose, "Export");
            Check(AssistantCommandRouter.Parse("显示全部") == AssistantCommand.ZoomToFit, "Zoom");
            Check(AssistantCommandRouter.Parse("HELP") == AssistantCommand.Help, "Help");

            MotionTests.Run(Check);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("artifacts");
            Directory.CreateDirectory(output);
            RenderAndCheck(output, 360, 820, false);
            RenderAndCheck(output, 360, 820, true);
            RenderAndCheck(output, 280, 700, true);
            Console.WriteLine($"PASS: {_checks} checks. UI previews: {output}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void RenderAndCheck(string output, int width, int height, bool withPlan)
    {
        using var pane = new AssistantPane { Size = new Size(width, height) };
        _ = pane.Handle;
        pane.CreateControl();
        pane.SetContext("装配：测试总装.SLDASM\r\n实例：输送模组-1\r\n配置：Default\r\n文件：D:\\示例项目\\输送模组.SLDASM");
        pane.AddMessage("助手", "你好，请在 SW 特征树中选择一个模组，然后输入“替换该模组”。");
        pane.AddMessage("你", "替换该模组");
        pane.AddMessage("助手", "替换计划已准备好。请选择目标配置，检查下方计划后点击“执行替换”。");
        if (withPlan)
        {
            pane.ShowPlan("原实例：输送模组-1\r\n目标：D:\\示例项目\\视觉输送模组.SLDASM\r\n仅此实例 · 尝试重接配合 · 不自动保存",
                new[] { "Default", "HighSpeed" }, "HighSpeed");
            var config = Descendants(pane).OfType<ComboBox>().Single();
            Check((string)config.SelectedItem == "HighSpeed", "Plan uses explicit target configuration");
            string? accepted = null;
            pane.ReplacementAccepted += value => accepted = value;
            // Calling OnClick avoids needing a visible desktop window for a control event smoke test.
            var execute = Descendants(pane).OfType<Button>().Single(b => b.Text == "执行替换");
            typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(execute, new object[] { EventArgs.Empty });
            Check(accepted == "HighSpeed", "Execute submits selected configuration");
            var motionClicks = 0;
            pane.MotionAccepted += () => motionClicks++;
            pane.ShowMotionPlan("绕总装 Z 轴旋转 90 度；中心：模组自身原点");
            Check(!config.Visible, "Motion plan hides replacement configuration");
            typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(execute, new object[] { EventArgs.Empty });
            Check(motionClicks == 1, "Motion button dispatches motion event");
            pane.ShowPlan("原实例：输送模组-1\r\n目标：视觉输送模组.SLDASM", new[] { "Default", "HighSpeed" }, "HighSpeed");
            Check(config.Visible && execute.Text == "执行替换", "Switching back restores replacement UI");

        }
        pane.PerformLayout();
        pane.SetBusy(true);
        Check(!Descendants(pane).OfType<Button>().Single(b => b.Text == "发送").Enabled, "No duplicate submissions while busy");
        pane.SetBusy(false);
        foreach (var control in Descendants(pane))
        {
            control.CreateControl();
            control.PerformLayout();
        }
        File.WriteAllLines(Path.Combine(output, $"layout-{width}-{withPlan}.txt"),
            Descendants(pane).Select(c => $"{c.GetType().Name}: {c.Text.Replace("\r\n", " / ")} bounds={c.Bounds} visible={c.Visible} font={c.Font.Size}"));
        using var bitmap = new Bitmap(width, height);
        pane.DrawToBitmap(bitmap, new Rectangle(0, 0, width, height));
        bitmap.Save(Path.Combine(output, $"assistant-{width}-{(withPlan ? "plan" : "chat")}.png"), ImageFormat.Png);
        var history = Descendants(pane).OfType<RichTextBox>().Single();
        Check(history.Height >= 80, "Chat remains usable with replacement plan");
        var send = Descendants(pane).OfType<Button>().Single(b => b.Text == "发送");
        Check(send.Bottom <= send.Parent!.ClientSize.Height, "Send button stays inside footer");
    }

    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + description);
        _checks++;
    }
}
