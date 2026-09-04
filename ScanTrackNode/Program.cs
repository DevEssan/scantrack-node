using ScanTrackNode.Graph;
using ScanTrackNode.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// IHttpClientFactory — används av NodeRegistry och PackageForwarder
builder.Services.AddHttpClient();

// Ladda stadsgrafen från CSV
var csvPath = Path.Combine(AppContext.BaseDirectory, "data", "cities.csv");
var graph = GraphLoader.Load(csvPath);
builder.Services.AddSingleton(new DijkstraService(graph));

builder.Services.AddSingleton<NodeRegistry>();
builder.Services.AddSingleton<PackageForwarder>();
builder.Services.AddSingleton<PackageStore>();
builder.Services.AddHostedService<HeartbeatService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();

// Registrera noden mot centralt register vid uppstart
var registry = app.Services.GetRequiredService<NodeRegistry>();
await registry.RegisterSelfAsync();

app.Run();
