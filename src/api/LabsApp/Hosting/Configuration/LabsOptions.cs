namespace LabsApp.Hosting.Configuration;

/// <summary>FR-030: параметр Labs__MaxSemester (подставляется во все проверки семестра).</summary>
public sealed class LabsOptions
{
    public const string SectionName = "Labs";

    public const string MaxSemesterVariable = "Labs__MaxSemester";

    public const int DefaultMaxSemester = 10;
    public const int MinMaxSemester = 1;
    public const int MaxMaxSemester = 100;

    public int MaxSemester { get; set; } = DefaultMaxSemester;
}
