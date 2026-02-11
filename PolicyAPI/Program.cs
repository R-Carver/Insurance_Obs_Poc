using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Dapr;
using Dapr.Client;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// In-memory store for now (we'll swap to DB later)
builder.Services.AddSingleton<PolicyStore>();
builder.Services.AddHostedService<PolicyWorker>(); // background worker for "async creation"
builder.Services.AddDaprClient();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

app.MapSubscribeHandler();

app.MapPost("/policies", ([FromBody] CreatePolicyRequest req, PolicyStore store) =>
    {
        var policyId = Guid.NewGuid().ToString("N");

        store.CreatePending(policyId, req.CustomerId, req.ProductCode);

        // Enqueue async work (later: publish to RabbitMQ via Dapr)
        store.Enqueue(policyId);

        return Results.Accepted($"/policies/{policyId}", new { policyId, status = "Pending" });
    })
    .WithName("CreatePolicy");
//.WithOpenApi();

app.MapGet("/policies/{policyId}", (string policyId, PolicyStore store) =>
    {
        return store.TryGet(policyId, out var policy)
            ? Results.Ok(policy)
            : Results.NotFound(new { message = "Policy not found" });
    })
    .WithName("GetPolicy");
//.WithOpenApi();

//app.Run("http://localhost:5082");
app.Run();

record CreatePolicyRequest(string CustomerId, string ProductCode);

class PolicyStore
{
    private readonly ConcurrentDictionary<string, PolicyDto> _policies = new();
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly Random _random = new();

    public void CreatePending(string policyId, string customerId, string productCode)
    {
        _policies[policyId] = new PolicyDto(
            policyId,
            customerId,
            productCode,
            Status: "Pending",
            Premium: null,
            FailureReason: null,
            UpdatedAtUtc: DateTime.UtcNow
        );
    }

    public void Enqueue(string policyId) => _queue.Enqueue(policyId);

    public bool TryDequeue(out string? policyId) => _queue.TryDequeue(out policyId);

    public bool TryGet(string policyId, out PolicyDto policy) => _policies.TryGetValue(policyId, out policy!);

    public void MarkCreated(string policyId, decimal premium)
    {
        if (_policies.TryGetValue(policyId, out var p))
            _policies[policyId] = p with { Status = "Created", Premium = premium, FailureReason = null, UpdatedAtUtc = DateTime.UtcNow };
    }

    public void MarkFailed(string policyId, string reason)
    {
        if (_policies.TryGetValue(policyId, out var p))
            _policies[policyId] = p with { Status = "Failed", Premium = null, FailureReason = reason, UpdatedAtUtc = DateTime.UtcNow };
    }

    public decimal CalculatePremium(string productCode)
    {
        // simple deterministic-ish premium
        var basePremium = productCode == "BASIC" ? 20 : productCode == "PLUS" ? 35 : 50;
        var jitter = _random.Next(0, 10);
        return basePremium + jitter;
    }

    public bool ShouldFail() => _random.NextDouble() < 0.15; // 15% failure for demo
}

record PolicyDto(
    string PolicyId,
    string CustomerId,
    string ProductCode,
    string Status,
    decimal? Premium,
    string? FailureReason,
    DateTime UpdatedAtUtc
);

class PolicyWorker : BackgroundService
{
    private readonly PolicyStore _store;

    public PolicyWorker(PolicyStore store) => _store = store;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_store.TryDequeue(out var policyId) && policyId is not null)
            {
                // simulate some processing time
                await Task.Delay(TimeSpan.FromMilliseconds(500 + Random.Shared.Next(0, 1200)), stoppingToken);

                if (_store.ShouldFail())
                    _store.MarkFailed(policyId, "Simulated underwriting failure.");
                else
                    _store.MarkCreated(policyId, _store.CalculatePremium("BASIC"));
            }
            else
            {
                await Task.Delay(100, stoppingToken);
            }
        }
    }
}
