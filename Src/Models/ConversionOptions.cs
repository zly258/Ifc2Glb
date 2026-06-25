using System.IO;

namespace Ifc2Glb;

internal sealed record ConversionOptions
{
    public required string InputPath { get; init; }

    public required string OutputPath { get; init; }

    public string TempDirectory { get; init; } = Path.GetTempPath();

    public bool IncludeSpaces { get; init; }

    public bool IncludeOpenings { get; init; }

    public bool AddYUpRootTransform { get; init; } = true;
}
