using System;
using System.Linq;
using AutoAssemblyAddin.Services;

internal static class MotionTests
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var text in new[] { "沿 X 轴移动 100 毫米", "沿x轴平移1cm", "将选中的模组沿 Y 轴负方向移动 2 厘米", "请绕自身Z轴旋转-90度", "绕局部x轴旋转.5deg", "沿全局Z轴移动0.1米" })
        {
            check(MotionCommand.TryParse(text, out _), "Parse " + text);
            check(AssistantCommandRouter.Parse(text) == AssistantCommand.MoveModule, "Route " + text);
        }
        foreach (var text in new[] { "不要沿X轴移动1mm", "沿X轴移动1mm？", "沿X轴移动1", "沿X轴移动NaNmm", "沿X轴移动1mm再绕Z轴旋转90度", "向左移动100毫米", "绕Z轴旋转1mm", "沿Z轴移动1度", "绕Y轴旋转90度\n沿X轴移动10mm", "沿X轴负方向移动-1毫米", "绕Z轴顺时针旋转90度", "旋转90度" })
            check(!MotionCommand.TryParse(text, out _), "Reject ambiguous/unsafe " + text);
        MotionCommand.TryParse("沿Y轴负方向移动2厘米", out var parsed);
        check(parsed!.Amount == -20 && parsed.Axis == 1 && !parsed.Rotate && !parsed.Local, "Signed unit conversion");
        check(AssistantCommandRouter.Parse("执行调整") == AssistantCommand.ExecuteMotion, "Execute route");
        check(AssistantCommandRouter.Parse("撤销上次调整") == AssistantCommand.UndoMotion, "Undo route");
        var identity = new[] { 1d, 0, 0, 0, 1d, 0, 0, 0, 1d, .2, .3, .4, 1d, 0, 0, 0 };
        for (var axis = 0; axis < 3; axis++)
        {
            var move = MotionMath.Apply(identity, new MotionCommand(false, false, axis, -25));
            check(Math.Abs(move[9 + axis] - identity[9 + axis] + .025) < 1e-12, "mm to metres axis " + axis);
            check(move.Take(9).SequenceEqual(identity.Take(9)), "Translation keeps orientation " + axis);
            var rotated = MotionMath.Apply(identity, new MotionCommand(true, false, axis, 90));
            var inverse = MotionMath.Apply(rotated, new MotionCommand(true, false, axis, -90));
            check(MotionMath.Equal(inverse, identity), "Rotation inverse " + axis);
            check(rotated.Skip(9).SequenceEqual(identity.Skip(9)), "Rotation preserves origin and scale " + axis);
        }
        var z = MotionMath.Apply(identity, new MotionCommand(true, false, 2, 90));
        check(Math.Abs(z[0]) < 1e-12 && Math.Abs(z[1] - 1) < 1e-12 && Math.Abs(z[3] + 1) < 1e-12, "Positive Z turns local X towards world Y");
        var localMove = MotionMath.Apply(z, new MotionCommand(false, true, 0, 10));
        check(Math.Abs(localMove[9] - .2) < 1e-12 && Math.Abs(localMove[10] - .31) < 1e-12, "Local X translation follows rotated axis");
        var world = MotionMath.Apply(z, new MotionCommand(true, false, 0, 90));
        var local = MotionMath.Apply(z, new MotionCommand(true, true, 0, 90));
        check(!MotionMath.Equal(world, local), "Local and world rotations have different multiplication order");
        check(Math.Abs(local[0] - z[0]) < 1e-12 && Math.Abs(local[1] - z[1]) < 1e-12 && Math.Abs(local[2] - z[2]) < 1e-12,
            "Rotation around local X keeps local X basis fixed");
        foreach (var command in new[] { new MotionCommand(false, false, 0, 0), new MotionCommand(true, false, 0, 360) })
        {
            var rejected = false;
            try { MotionMath.Apply(identity, command); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, "No-op command rejected");
        }
        check(identity[9] == .2 && identity[0] == 1, "Inputs remain unchanged");
    }
}
