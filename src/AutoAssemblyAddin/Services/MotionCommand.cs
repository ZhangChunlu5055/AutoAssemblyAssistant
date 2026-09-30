using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AutoAssemblyAddin.Services;

internal sealed class MotionCommand
{
    public bool Rotate { get; }
    public bool Local { get; }
    public int Axis { get; }
    // Millimetres for translation, degrees for rotation; signed right-hand convention.
    public double Amount { get; }
    public MotionCommand(bool rotate, bool local, int axis, double amount)
    {
        Rotate = rotate; Local = local; Axis = axis; Amount = amount;
    }
    public string Description => $"{(Local ? "模组自身" : "总装")}{"XYZ"[Axis]} 轴{(Rotate ? "旋转" : "移动")} {Amount.ToString("0.########", CultureInfo.InvariantCulture)} {(Rotate ? "度" : "毫米")}";

    public static bool TryParse(string? text, out MotionCommand? command)
    {
        command = null;
        var value = (text ?? "").Trim().TrimEnd('。', '！', '!').Trim();
        // Anchored grammar: never accept negation, multiple actions, missing units or screen-relative directions.
        var match = Regex.Match(value,
            @"\A(?:请\s*)?(?:(?:将|把)\s*(?:选中(?:的)?|该|这个)?(?:模组|组件)\s*)?(?<verb>沿|绕)\s*(?<frame>总装|全局|自身|本地|局部)?\s*(?<axis>[XYZ])\s*轴\s*(?<direction>正向|负向|正方向|负方向)?\s*(?<action>移动|平移|旋转)\s*(?<amount>[+-]?(?:[0-9]+(?:\.[0-9]+)?|\.[0-9]+))\s*(?<unit>毫米|厘米|米|mm|cm|m|度|°|deg)\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) return false;
        var rotate = match.Groups["action"].Value == "旋转";
        if ((match.Groups["verb"].Value == "绕") != rotate) return false;
        var unit = match.Groups["unit"].Value.ToLowerInvariant();
        var angleUnit = unit == "度" || unit == "°" || unit == "deg";
        if (angleUnit != rotate) return false;
        if (!double.TryParse(match.Groups["amount"].Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var amount) || double.IsNaN(amount) || double.IsInfinity(amount)) return false;
        var direction = match.Groups["direction"].Value;
        if (direction.Length > 0)
        {
            if (match.Groups["amount"].Value.StartsWith("-") || match.Groups["amount"].Value.StartsWith("+")) return false;
            if (direction.StartsWith("负")) amount = -amount;
        }
        if (!rotate) amount *= unit == "米" || unit == "m" ? 1000 : unit == "厘米" || unit == "cm" ? 10 : 1;
        if (double.IsInfinity(amount)) return false;
        var frame = match.Groups["frame"].Value;
        command = new MotionCommand(rotate, frame == "自身" || frame == "本地" || frame == "局部",
            "XYZ".IndexOf(match.Groups["axis"].Value.ToUpperInvariant(), StringComparison.Ordinal), amount);
        return true;
    }
}
