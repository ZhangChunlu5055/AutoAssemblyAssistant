using System;
using System.Linq;

namespace AutoAssemblyAddin.Services;

internal static class MotionMath
{
    public static double[] Apply(double[] before, MotionCommand command)
    {
        Validate(before);
        if (command.Axis < 0 || command.Axis > 2 || double.IsNaN(command.Amount) || double.IsInfinity(command.Amount))
            throw new InvalidOperationException("移动/旋转参数无效。");
        var after = (double[])before.Clone();
        if (!command.Rotate)
        {
            var metres = command.Amount / 1000;
            for (var i = 0; i < 3; i++)
                after[9 + i] += metres * (command.Local ? before[command.Axis * 3 + i] : (i == command.Axis ? 1 : 0));
        }
        else
        {
            var angle = (command.Amount % 360) * Math.PI / 180;
            var c = Math.Cos(angle);
            var s = Math.Sin(angle);
            // SW stores row-vector basis axes in indices 0..8, translation in 9..11, scale in 12.
            var delta = command.Axis == 0 ? new[] { 1d, 0, 0, 0, c, s, 0, -s, c } :
                command.Axis == 1 ? new[] { c, 0, -s, 0, 1d, 0, s, 0, c } :
                new[] { c, s, 0, -s, c, 0, 0, 0, 1d };
            var left = command.Local ? delta : before;
            var right = command.Local ? before : delta;
            for (var row = 0; row < 3; row++)
                for (var col = 0; col < 3; col++)
                    after[row * 3 + col] = Enumerable.Range(0, 3).Sum(k => left[row * 3 + k] * right[k * 3 + col]);
            // Keep the component origin stationary, including when rotating about global axes.
        }
        Validate(after);
        if (Equal(before, after)) throw new InvalidOperationException("该指令不会产生可检测的位姿变化，请输入非零距离或有效角度。");
        return after;
    }

    public static bool Equal(double[] left, double[] right)
    {
        if (left.Length != 16 || right.Length != 16) return false;
        return Enumerable.Range(0, 13).All(i => !double.IsNaN(left[i]) && !double.IsNaN(right[i]) &&
            Math.Abs(left[i] - right[i]) <= (i >= 9 && i <= 11 ? 1e-7 : 1e-8));
    }

    public static void Validate(double[] data)
    {
        if (data.Length != 16 || data.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
            throw new InvalidOperationException("无法读取有效的模组位姿。");
        var determinant = data[0] * (data[4] * data[8] - data[5] * data[7]) -
            data[1] * (data[3] * data[8] - data[5] * data[6]) + data[2] * (data[3] * data[7] - data[4] * data[6]);
        if (Math.Abs(data[12] - 1) > 1e-8 || Math.Abs(determinant - 1) > 1e-6)
            throw new InvalidOperationException("当前版本不调整带缩放或镜像变换的模组。");
    }
}
