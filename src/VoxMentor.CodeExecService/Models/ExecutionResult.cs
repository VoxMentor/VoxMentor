namespace VoxMentor.CodeExecService.Models;

public class ExecutionResult
{
    public string Stdout { get; set; } = string.Empty;
    public string Stderr { get; set; } = string.Empty;
    public int? ExecutionTimeMs { get; set; }
    public int? MemoryUsageKb { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool TimedOut { get; set; }
    public bool AllTestCasesPassed { get; set; }
    public List<TestCaseResult>? TestResults { get; set; }
}

public class TestCaseResult
{
    public int TestCaseIndex { get; set; }
    public string Expected { get; set; } = string.Empty;
    public string Actual { get; set; } = string.Empty;
    public bool Passed { get; set; }
}
