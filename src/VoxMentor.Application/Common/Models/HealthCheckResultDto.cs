namespace VoxMentor.Application.Common.Models;

public class HealthCheckResultDto
{
    public string Status { get; set; } = "Healthy";
    public IDictionary<string, string> Checks { get; set; } = new Dictionary<string, string>();
}
