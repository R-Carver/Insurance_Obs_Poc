using Frontend.Components;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Add Blazor services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// HttpClients for backend services
builder.Services.AddHttpClient("precheck", c =>
    c.BaseAddress = new Uri(builder.Configuration["Services:PrecheckBaseUrl"]!));

builder.Services.AddHttpClient("policy", c =>
    c.BaseAddress = new Uri(builder.Configuration["Services:PolicyBaseUrl"]!));

// OTel
builder.Services.AddOpenTelemetry()
    .WithTracing(t =>
    {
        t.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("frontend"));
        t.SetSampler(new AlwaysOnSampler()); // important for debugging
        t.AddAspNetCoreInstrumentation();
        t.AddHttpClientInstrumentation();
        t.AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri("http://otel-collector.insurance.svc.cluster.local:4317");
            o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
        });
    });

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();

app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/api/demo/create-policy-flow", async (DemoPolicyRequest req, IHttpClientFactory httpFactory) =>
{
    var precheckClient = httpFactory.CreateClient("precheck");
    var policyClient = httpFactory.CreateClient("policy");

    // 1) precheck
    var precheckResponse = await precheckClient.PostAsJsonAsync("/precheck", new
    {
        customerId = req.CustomerId,
        productCode = req.ProductCode,
        age = req.Age,
        country = req.Country,
    });

    if (!precheckResponse.IsSuccessStatusCode)
    {
        return Results.BadRequest(new
        {
            step = "precheck",
            error = $"Precheck request failed with status {(int)precheckResponse.StatusCode}"
        });
    }

    var precheck = await precheckResponse.Content.ReadFromJsonAsync<PrecheckResponse>();

    if (precheck is null)
    {
        return Results.BadRequest(new
        {
            step = "precheck",
            error = "Precheck response could not be parsed"
        });
    }

    if (!string.Equals(precheck.Decision, "Approved", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Ok(new
        {
            status = "Rejected",
            precheck = precheck
        });
    }
    
    // 2) create policy
    var createResponse = await policyClient.PostAsJsonAsync("/policies", new
    {
        customerId = req.CustomerId,
        productCode = req.ProductCode,
        age = req.Age,
        country = req.Country,
    });

    if (!createResponse.IsSuccessStatusCode)
    {
        return Results.BadRequest(new
        {
            step = "policy-create",
            error = $"Policy create failed with status {(int)createResponse.StatusCode}"
        });
    }

    var created = await createResponse.Content.ReadFromJsonAsync<CreatePolicyResponse>();

    if (created is null || string.IsNullOrWhiteSpace(created.PolicyId))
    {
        return Results.BadRequest(new
        {
            step = "policy-create",
            error = "Policy create response could not be parsed"
        });
    }
    
    // 3) poll policy status
    PolicyStatusResponse? finalStatus = null;

    for (var i = 0; i < 10; i++)
    {
        await Task.Delay(500);

        var pollResponse = await policyClient.GetAsync($"/policies/{created.PolicyId}");

        if (!pollResponse.IsSuccessStatusCode)
            continue;

        finalStatus = await pollResponse.Content.ReadFromJsonAsync<PolicyStatusResponse>();

        if (finalStatus is not null &&
            (finalStatus.Status == "Created" || finalStatus.Status == "Failed"))
        {
            break;
        }
    }

    return Results.Ok(new
    {
        policyId = created.PolicyId,
        precheck = precheck,
        policy = finalStatus
    });
});

//app.Run("http://localhost:5080");
app.Run();

record DemoPolicyRequest(string CustomerId, string ProductCode, int Age, string Country);

record PrecheckResponse(string Decision, string[]? Reasons);

record CreatePolicyResponse(string PolicyId, string Status);

record PolicyStatusResponse(
    string PolicyId,
    string Status,
    decimal? Premium,
    string? FailureReason
);