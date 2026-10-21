using LineWatch.Api.Collector;
using LineWatch.Api.Data;
using LineWatch.Api.Endpoints;
using LineWatch.Api.Realtime;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<CollectorOptions>(builder.Configuration.GetSection(CollectorOptions.Section));
builder.Services.AddDbContext<LineWatchDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("LineWatch")));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LiveStatus>();
builder.Services.AddHostedService<ModbusPoller>();
builder.Services.AddHostedService<ReadingRetention>();

builder.Services.AddSignalR();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<LineWatchDbContext>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

// Fine for a single-instance demo; with several replicas migrations would run from the pipeline instead.
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<LineWatchDbContext>().Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseCors();

app.MapMachineEndpoints();
app.MapAlarmEndpoints();
app.MapHub<TelemetryHub>("/hubs/telemetry");
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
