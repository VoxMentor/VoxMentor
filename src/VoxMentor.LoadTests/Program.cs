using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NBomber.Contracts;
using NBomber.CSharp;

var baseUrl = Environment.GetEnvironmentVariable("VOXMENTOR_BASE_URL") ?? "http://localhost:5253";

// Pool of accounts that must be pre-registered against the target Core API
// (see docs/load-test-results.md). Each virtual user is assigned one account
// round-robin by a static counter and gets its own cookie-enabled HttpClient
// so sessions are never shared across users.
var accounts = new (string Email, string Password)[]
{
    ("loadtest1@voxmentor.local", "LoadTest!2026"),
    ("loadtest2@voxmentor.local", "LoadTest!2026"),
    ("loadtest3@voxmentor.local", "LoadTest!2026"),
    ("loadtest4@voxmentor.local", "LoadTest!2026"),
    ("loadtest5@voxmentor.local", "LoadTest!2026"),
};

var clients = accounts.Select(a =>
{
    var handler = new HttpClientHandler
    {
        UseCookies = true,
        AllowAutoRedirect = false,
        MaxConnectionsPerServer = 50,
    };
    return new { Account = a, Client = new HttpClient(handler)
    {
        BaseAddress = new Uri(baseUrl),
        Timeout = TimeSpan.FromSeconds(30),
    } };
}).ToArray();

var accountCount = accounts.Length;
int _userCounter = 0;

static async Task<HttpResponseMessage> PostJson(HttpClient client, string path, object? body)
{
    var req = new HttpRequestMessage(HttpMethod.Post, path)
    {
        Content = body is null ? null : JsonContent.Create(body),
    };
    return await client.SendAsync(req);
}

// Logs in, then refreshes. Returns true only when both calls succeed.
static async Task<bool> LoginAsync(HttpClient client, string email, string password)
{
    var loginResp = await PostJson(client, "/api/v1/auth/login", new { Email = email, Password = password });
    if (!loginResp.IsSuccessStatusCode)
        return false;
    var refreshResp = await PostJson(client, "/api/v1/auth/refresh", null);
    return refreshResp.IsSuccessStatusCode;
}

// Fetches a real question ID from the bank so the practice scenario reaches
// answer submission instead of a 404 on a non-existent question. Falls back to
// a generated ID when the bank is empty or unreachable.
static async Task<string> GetValidQuestionIdAsync(HttpClient client)
{
    using var resp = await client.GetAsync("/api/v1/questions?page=1&pageSize=20");
    if (!resp.IsSuccessStatusCode)
        return Guid.NewGuid().ToString();
    using var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>();
    if (doc is null || !doc.RootElement.TryGetProperty("data", out var data) ||
        !data.TryGetProperty("questions", out var questions))
        return Guid.NewGuid().ToString();
    // Prefer a code question with at least one test case so the scenario
    // actually reaches Judge0 instead of a not-found path.
    foreach (var q in questions.EnumerateArray())
    {
        if (!q.TryGetProperty("questionType", out var type) ||
            type.GetString() != "Code")
            continue;
        if (!q.TryGetProperty("id", out var id) || id.ValueKind == JsonValueKind.Null)
            continue;
        if (q.TryGetProperty("totalTestCases", out var totalCases) &&
            totalCases.TryGetInt32(out var n) && n > 0)
            return id.GetString() ?? Guid.NewGuid().ToString();
    }
    // No qualifying question found; fall back to any question with an id.
    foreach (var q in questions.EnumerateArray())
    {
        if (q.TryGetProperty("id", out var id) && id.ValueKind != JsonValueKind.Null)
            return id.GetString() ?? Guid.NewGuid().ToString();
    }
    return Guid.NewGuid().ToString();
}

// ---------- Scenarios ----------

// Auth: login then refresh (cookies carried on the same client).
var authScenario = Scenario.Create("auth_login_refresh", async _ =>
{
    var idx = Interlocked.Increment(ref _userCounter) % accountCount;
    var client = clients[idx].Client;
    var acct = accounts[idx];
    return await LoginAsync(client, acct.Email, acct.Password) ? Response.Ok() : Response.Fail();
});

// Practice: login, fetch a real question, then submit code.
var practiceScenario = Scenario.Create("practice_submit_code", async _ =>
{
    var idx = Interlocked.Increment(ref _userCounter) % accountCount;
    var client = clients[idx].Client;
    var acct = accounts[idx];

    if (!await LoginAsync(client, acct.Email, acct.Password))
        return Response.Fail();

    var questionId = await GetValidQuestionIdAsync(client);

    return await PostJson(client, "/api/v1/student/submit-code", new
    {
        questionId,
        code = "def solve(nums):\n    return sum(nums)",
        language = "python",
    }) is { IsSuccessStatusCode: true } ? Response.Ok() : Response.Fail();
});

// Mock interview: login, start (capture the real interviewId from the
// ApiResponse envelope), then answer it.
var mockScenario = Scenario.Create("mock_interview_start_answer", async _ =>
{
    var idx = Interlocked.Increment(ref _userCounter) % accountCount;
    var client = clients[idx].Client;
    var acct = accounts[idx];

    if (!await LoginAsync(client, acct.Email, acct.Password))
        return Response.Fail();

    var startResp = await PostJson(client, "/api/v1/mock/start", new { type = "Technical", voiceMode = false });
    if (!startResp.IsSuccessStatusCode)
        return Response.Fail();

    var startBody = await startResp.Content.ReadFromJsonAsync<JsonDocument?>();
    var interviewId = Guid.Empty.ToString();
    if (startBody is { } sb && sb.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("interviewId", out var id))
        interviewId = id.GetString() ?? Guid.Empty.ToString();

    return await PostJson(client, $"/api/v1/mock/{interviewId}/answer", new
    {
        message = "I would use Kadane's algorithm — track the running sum and reset on negatives.",
    }) is { IsSuccessStatusCode: true } ? Response.Ok() : Response.Fail();
});

// Per-account tutor admission gate: each account sends at most one ask per
// 6 minutes (tutor rate limit is 10 requests/hour/student). A copy whose
// account slot is free sends immediately and reserves the next slot (+6
// minutes). Any other copy returns Ok without calling the API — an
// intentional local drop, not an API failure: no wait is ever queued, so no
// iteration can outlive or cross a load-simulation phase and nothing needs
// cancellation. The gate is per-process and resets between runs.
var tutorNextSlot = new DateTime[accountCount];
var tutorLocks = Enumerable.Range(0, accountCount).Select(_ => new object()).ToArray();

// AI Coach: login then ask (omit optional conceptId so AskTutorHandler can
// proceed to session creation without requiring a concept).
var tutorScenario = Scenario.Create("tutor_ask", async _ =>
{
    var idx = Interlocked.Increment(ref _userCounter) % accountCount;
    var client = clients[idx].Client;
    var acct = accounts[idx];

    if (!await LoginAsync(client, acct.Email, acct.Password))
        return Response.Fail();

    TimeSpan wait;
    bool dropped;
    lock (tutorLocks[idx])
    {
        var now = DateTime.UtcNow;
        var next = tutorNextSlot[idx];
        wait = next > now ? next - now : TimeSpan.Zero;
        if (wait <= TimeSpan.Zero)
            tutorNextSlot[idx] = now + TimeSpan.FromMinutes(6);
        dropped = wait > TimeSpan.Zero;
    }
    // Intentional local drop, not an API failure: the account's quota slot was
    // taken by another copy, so no request is sent and no wait is queued —
    // this keeps iterations inside their load-simulation phase (nothing to
    // cancel) and avoids NBomber restart-on-fail loops.
    if (dropped)
        return Response.Ok();

    return await PostJson(client, "/api/v1/tutor/ask", new
    {
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
Scenario.WithoutWarmUp(tutorScenario);

var ctx = NBomberRunner.RegisterScenarios(authScenario, practiceScenario, mockScenario, tutorScenario);

NBomberRunner.WithReportFormats(ctx, NBomber.Contracts.Stats.ReportFormat.Html | NBomber.Contracts.Stats.ReportFormat.Csv);
NBomberRunner.WithReportFolder(ctx, "results");
NBomberRunner.Run(ctx);
