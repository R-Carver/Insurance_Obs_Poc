using Frontend.Components;

var builder = WebApplication.CreateBuilder(args);

// Add Blazor services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// HttpClients for backend services
builder.Services.AddHttpClient("precheck", c =>
    c.BaseAddress = new Uri(builder.Configuration["Services:PrecheckBaseUrl"]!));

builder.Services.AddHttpClient("policy", c =>
    c.BaseAddress = new Uri(builder.Configuration["Services:PolicyBaseUrl"]!));

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();

app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

//app.Run("http://localhost:5080");
app.Run();