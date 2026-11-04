using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NBomber.Contracts;
using NBomber.CSharp;

var baseUrl = Environment.GetEnvironmentVariable("VOXMENTOR_BASE_URL") ?? "http://localhost:5253";

// Cookie-enabled client so the HttpOnly access_token / refresh_token set by login
// are replayed on subsequent requests within the same simulated user.
// NBomber + NBomber.Http are referenced; requests are issued via System.Net.Http.
var handler = new HttpClientHandler
{
    UseCookies = true,
    AllowAutoRedirect = false,
    MaxConnectionsPerServer = 200,
};
var client = new HttpClient(handler)
{
    BaseAddress = new Uri(baseUrl),
    Timeout = TimeSpan.FromSeconds(30),
};

const string Email = "loadtest@voxmentor.local";
const string Password = "LoadTest!2026";

static async Task<HttpResponseMessage> PostJson(HttpClient client, string path, object? body)
{
    var req = new HttpRequestMessage(HttpMethod.Post, path)
    {
        Content = body is null ? null : JsonContent.Create(body),
    };
    return await client.SendAsync(req);
}

// ---------- Scenarios ----------

// Auth: login then refresh (cookies carried on the same client).
var authScenario = Scenario.Create("auth_login_refresh", async context =>
{
    await PostJson(client, "/api/v1/auth/login", new { Email, Password });
    await PostJson(client, "/api/v1/auth/refresh", null);
    return Response.Ok();
});

// Practice: login then submit code.
var practiceScenario = Scenario.Create("practice_submit_code", async context =>
{
    await PostJson(client, "/api/v1/auth/login", new { Email, Password });
    return await PostJson(client, "/api/v1/student/submit-code", new
    {
        questionId = Guid.NewGuid().ToString(),
        code = "def solve(nums):\n    return sum(nums)",
        language = "python",
    }) is { IsSuccessStatusCode: true } ? Response.Ok() : Response.Fail();
});

// Mock interview: login, start (capture the real interviewId), then answer it.
var mockScenario = Scenario.Create("mock_interview_start_answer", async context =>
{
    await PostJson(client, "/api/v1/auth/login", new { Email, Password });
    var startResp = await PostJson(client, "/api/v1/mock/start", new { type = "Technical", voiceMode = false });
    if (!startResp.IsSuccessStatusCode)
        return Response.Fail();
    var startBody = await startResp.Content.ReadFromJsonAsync<JsonDocument?>();
    var interviewId = startBody?.RootElement.GetProperty("interviewId").GetString()
        ?? Guid.Empty.ToString();
    return await PostJson(client, $"/api/v1/mock/{interviewId}/answer", new
    {
        message = "I would use Kadane's algorithm — track the running sum and reset on negatives.",
    }) is { IsSuccessStatusCode: true } ? Response.Ok() : Response.Fail();
});

// AI Coach: login then ask.
var tutorScenario = Scenario.Create("tutor_ask", async context =>
{
    await PostJson(client, "/api/v1/auth/login", new { Email, Password });
    return await PostJson(client, "/api/v1/tutor/ask", new
    {
        conceptId = Guid.NewGuid().ToString(),
        question = "Why is Kadane's algorithm O(n)?",
    }) is { IsSuccessStatusCode: true } ? Response.Ok() : Response.Fail();
});

// ---------- Load simulations ----------

// Sustained baseline: fixed concurrency held for the duration (closed system).
var sustained = Simulation.KeepConstant(copies: 50, during: TimeSpan.FromMinutes(5));

// Spike: high injection rate over a short window (open system).
var spike = Simulation.RampingInject(rate: 200, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(2));

var sims = new NBomber.Contracts.LoadSimulation[] { spike, sustained };
Scenario.WithLoadSimulations(authScenario, sims);
Scenario.WithLoadSimulations(practiceScenario, sims);
Scenario.WithLoadSimulations(mockScenario, sims);
Scenario.WithLoadSimulations(tutorScenario, sims);

var ctx = NBomberRunner.RegisterScenarios(authScenario, practiceScenario, mockScenario, tutorScenario);

NBomberRunner.WithReportFormats(ctx, NBomber.Contracts.Stats.ReportFormat.Html | NBomber.Contracts.Stats.ReportFormat.Csv);
NBomberRunner.WithReportFolder(ctx, "results");
NBomberRunner.Run(ctx);
