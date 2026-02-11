using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

app.MapPost("/precheck", ([FromBody] PrecheckRequest req) =>
    {
        var reasons = new List<string>();

        if (req.Age < 18) reasons.Add("Customer must be at least 18.");
        if (string.IsNullOrWhiteSpace(req.Country)) reasons.Add("Country is required.");
        if (req.Country is not ("DE" or "AT" or "CH")) reasons.Add("Country not supported.");
        if (string.IsNullOrWhiteSpace(req.ProductCode)) reasons.Add("ProductCode is required.");

        var decision =
            reasons.Count > 0 ? "Rejected" :
            req.Age >= 75 ? "Manual" :
            "Approved";

        return Results.Ok(new PrecheckResponse(decision, reasons));
    })
    .WithName("Precheck");
    //.WithOpenApi();

//app.Run("http://localhost:5081");
app.Run();

record PrecheckRequest(string CustomerId, int Age, string Country, string ProductCode);
record PrecheckResponse(string Decision, List<string> Reasons);