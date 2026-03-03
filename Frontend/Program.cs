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

//app.Run("http://localhost:5080");
app.Run();