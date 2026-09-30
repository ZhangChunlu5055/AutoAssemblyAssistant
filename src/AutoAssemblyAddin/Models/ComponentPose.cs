using System;
using System.Collections.Generic;

namespace AutoAssemblyAddin.Models;

public sealed class ComponentPose
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public double[] Transform { get; set; } = Array.Empty<double>();
}

public sealed class PoseExportDocument
{
    public string SourceAssembly { get; set; } = string.Empty;
    public string ExportedAt { get; set; } = string.Empty;
    public List<ComponentPose> Components { get; set; } = new();
}
