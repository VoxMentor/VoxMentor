using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoxMentor.CodeExecService.Clients;

public class Judge0Client
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public Judge0Client(HttpClient http, IConfiguration config)
    {
        _http = http;
        _baseUrl = config["Judge0:BaseUrl"] ?? "http://localhost:2358";
    }

    public async Task<Judge0Response> ExecuteAsync(
        string sourceCode, int languageId, string stdin = "",
        CancellationToken cancellationToken = default)
    {
        var request = new
        {
            source_code = sourceCode,
            language_id = languageId,
            stdin = stdin,
            cpu_time_limit = 10,
            wall_time_limit = 10,
            memory_limit = 256000
        };

        var response = await _http.PostAsJsonAsync($"{_baseUrl}/submissions", request, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        var submission = await response.Content.ReadFromJsonAsync<Judge0Submission>(JsonOptions, cancellationToken);

        if (submission?.Token == null)
            throw new InvalidOperationException("Judge0 returned null or empty submission token");

        return await PollResultAsync(submission.Token, cancellationToken);
    }

    private async Task<Judge0Response> PollResultAsync(string token, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        try
        {
            while (!linkedCts.Token.IsCancellationRequested)
            {
                var response = await _http.GetAsync(
                    $"{_baseUrl}/submissions/{token}?base64_encoded=false",
                    linkedCts.Token);

                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<Judge0Response>(
                    JsonOptions, linkedCts.Token);

                if (result?.Status?.Id >= 3)
                    return result;

                await Task.Delay(500, linkedCts.Token);
            }
        }
        catch (OperationCanceledException) when (!timeoutCts.IsCancellationRequested)
        {
            throw;
        }

        if (!timeoutCts.IsCancellationRequested)
            linkedCts.Token.ThrowIfCancellationRequested();

        return new Judge0Response
        {
            Status = new Judge0Status { Id = 5, Description = "Time Limit Exceeded" },
            Time = "10",
            Stderr = "Execution timed out after 10 seconds"
        };
    }
}

public class Judge0Submission
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }
}

public class Judge0Response
{
    [JsonPropertyName("stdout")]
    public string? Stdout { get; set; }

    [JsonPropertyName("stderr")]
    public string? Stderr { get; set; }

    [JsonPropertyName("compile_output")]
    public string? CompileOutput { get; set; }

    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("memory")]
    public int? Memory { get; set; }

    [JsonPropertyName("status")]
    public Judge0Status? Status { get; set; }
}

public class Judge0Status
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
