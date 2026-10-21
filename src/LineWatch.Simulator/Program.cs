using LineWatch.Simulator;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<SimulatorOptions>(builder.Configuration.GetSection(SimulatorOptions.Section));
builder.Services.AddHostedService<ModbusSimulator>();

builder.Build().Run();
