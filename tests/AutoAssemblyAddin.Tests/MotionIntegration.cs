using System;
using System.IO;
using System.Linq;
using System.Text;
using AutoAssemblyAddin.Services;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static class MotionIntegration
{
    public static void Run(ISldWorks app, ModelDoc2 model, string targetPath, string directory)
    {
        var report = new StringBuilder();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("MOTION FAILED: " + message);
            Console.WriteLine("MOTION PASS: " + message);
            report.AppendLine("PASS: " + message);
        }
        var assembly = (AssemblyDoc)model;
        var components = ((object[])assembly.GetComponents(true)).OfType<Component2>().ToArray();
        var target = components.Single(c => SelectionContextService.SamePath(c.GetPathName(), targetPath));
        var peer = components.Single(c => !SelectionContextService.SameObject(c, target));
        void ReloadComponents()
        {
            components = ((object[])assembly.GetComponents(true)).OfType<Component2>().ToArray();
            target = components.Single(c => SelectionContextService.SamePath(c.GetPathName(), targetPath));
            peer = components.Single(c => !SelectionContextService.SameObject(c, target));
        }
        void Select(Component2 component)
        {
            model.ClearSelection2(true);
            Check(component.Select4(false, null, false), "Selected " + component.Name2);
        }
        Select(target);
        assembly.FixComponent();
        Check(target.IsFixed(), "Fixture starts with a fixed target");
        var activeConfiguration = model.ConfigurationManager.ActiveConfiguration.Name;
        model.ConfigurationManager.AddConfiguration("Untouched", "", "", 0, "", "");
        Check(model.ShowConfiguration2(activeConfiguration), "Returned to test configuration");
        ReloadComponents();
        Select(target);
        var original = ModuleMotionService.ReadPose(target);
        var peerOriginal = ModuleMotionService.ReadPose(peer);
        var service = new ModuleMotionService(app, new SelectionContextService(app));
        MotionPlan Prepare(string text)
        {
            Check(MotionCommand.TryParse(text, out var command), "Parsed " + text);
            return service.Prepare(command!);
        }
        ReplacementIntegration.SelectModuleFace(model, target);
        report.AppendLine(service.Execute(Prepare("沿X轴移动25毫米")));
        var moved = ModuleMotionService.ReadPose(target);
        Check(Math.Abs(moved[9] - original[9] - .025) < 1e-7, "Fixed target moved exactly 25 mm in world X");
        Check(target.IsFixed(), "Original fixed state restored");
        Check(MotionMath.Equal(peerOriginal, ModuleMotionService.ReadPose(peer)), "Other instance did not move");
        Check(model.ShowConfiguration2("Untouched"), "Opened untouched configuration");
        ReloadComponents();
        Check(MotionMath.Equal(original, ModuleMotionService.ReadPose(target)) && target.IsFixed(), "Other configuration keeps original pose and fixed state");
        Check(model.ShowConfiguration2(activeConfiguration), "Restored active configuration");
        ReloadComponents();
        Select(target);
        ReplacementIntegration.SelectModuleFace(model, target);
        report.AppendLine(service.Execute(Prepare("绕Z轴旋转90度")));
        var rotated = ModuleMotionService.ReadPose(target);
        Check(Enumerable.Range(9, 3).All(i => Math.Abs(rotated[i] - moved[i]) < 1e-7), "Rotation keeps component origin stationary");
        // Verify SW's actual MathVector convention, independently of MotionMath's composition.
        var vector = (MathVector)((MathUtility)app.GetMathUtility()).CreateVector(new[] { 1d, 0, 0 });
        var actualX = (double[])((MathVector)vector.MultiplyTransform(target.Transform2)).ArrayData;
        Check(Math.Abs(actualX[0] + moved[1]) < 1e-7 && Math.Abs(actualX[1] - moved[0]) < 1e-7 && Math.Abs(actualX[2] - moved[2]) < 1e-7,
            "SW confirms positive Z rotation by right-hand rule");
        report.AppendLine(service.Execute(service.PrepareUndo()));
        Check(MotionMath.Equal(moved, ModuleMotionService.ReadPose(target)), "Undo restores previous orientation and position");
        service.Execute(Prepare("绕Z轴旋转90度"));
        var localBefore = ModuleMotionService.ReadPose(target);
        service.Execute(Prepare("沿自身X轴移动10毫米"));
        var localAfter = ModuleMotionService.ReadPose(target);
        Check(Enumerable.Range(0, 3).All(i => Math.Abs(localAfter[9 + i] - localBefore[9 + i] - localBefore[i] * .01) < 1e-7),
            "Local translation follows rotated component axis");
        var stale = Prepare("沿Z轴移动1毫米");
        service.Execute(Prepare("沿X轴移动2毫米"));
        var rejected = false;
        try { service.Validate(stale); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Changed pose invalidates prepared plan");

        Select(target);
        assembly.UnfixComponent();
        Select(target);
        service.Execute(Prepare("沿Z轴移动5毫米"));
        Check(!target.IsFixed(), "Floating target remains floating");
        Select(peer);
        assembly.FixComponent();
        model.ClearSelection2(true);
        var selectData = ((SelectionMgr)model.SelectionManager).CreateSelectData();
        selectData.Mark = 1;
        Check(target.Select4(false, selectData, false) && peer.Select4(true, selectData, false), "Lock mate components selected with mark 1");
        var mateError = 0;
        var mate = assembly.AddMate5((int)swMateType_e.swMateLOCK, (int)swMateAlign_e.swMateAlignALIGNED, false,
            0, 0, 0, 1, 1, 0, 0, 0, false, false, 0, out mateError);
        Check(mate != null, "Real lock mate created between target and fixed peer; error=" + mateError);
        Check(model.EditRebuild3(), "Mated fixture rebuilds");
        Select(target);
        var constrainedBefore = ModuleMotionService.ReadPose(target);
        var constrainedPeerBefore = ModuleMotionService.ReadPose(peer);
        var failure = "";
        try { service.Execute(Prepare("沿X轴移动10毫米")); }
        catch (InvalidOperationException ex) { failure = ex.Message; }
        Check(failure.Contains("已校验恢复"), "Impossible constrained movement reports verified restoration");
        Check(MotionMath.Equal(constrainedBefore, ModuleMotionService.ReadPose(target)) &&
            MotionMath.Equal(constrainedPeerBefore, ModuleMotionService.ReadPose(peer)), "Constraint failure leaves both original poses");

        Select(peer);
        assembly.UnfixComponent();
        Select(target);
        failure = "";
        try { service.Execute(Prepare("沿Y轴移动10毫米")); }
        catch (InvalidOperationException ex) { failure = ex.Message; }
        Console.WriteLine("Linked movement feedback: " + failure);
        Console.WriteLine("Target restored=" + MotionMath.Equal(constrainedBefore, ModuleMotionService.ReadPose(target)) + "; peer restored=" + MotionMath.Equal(constrainedPeerBefore, ModuleMotionService.ReadPose(peer)));
        Check(failure.Contains("已校验恢复"), "Linked movement of a floating peer is rejected and restored");
        Check(MotionMath.Equal(constrainedBefore, ModuleMotionService.ReadPose(target)) &&
            MotionMath.Equal(constrainedPeerBefore, ModuleMotionService.ReadPose(peer)), "Both linked components returned to original poses");
        File.WriteAllText(Path.Combine(directory, "motion-result.txt"), report.ToString());
    }
}
