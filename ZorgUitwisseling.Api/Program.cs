using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using ZorgUitwisseling.Api.Common.Errors;
using ZorgUitwisseling.Api.Data;
using ZorgUitwisseling.Api.Endpoints;
using ZorgUitwisseling.Api.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(
    builder.Configuration.GetConnectionString("ExchangeDatabase")
        ?? "Data Source=zorguitwisseling.db;Foreign Keys=True"));
builder.Services.AddScoped<ExchangeService>();

WebApplication app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("ZorgUitwisseling API"));
}

app.MapExchangeEndpoints();

await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    await DatabaseInitializer.InitializeAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
}

app.Run();
