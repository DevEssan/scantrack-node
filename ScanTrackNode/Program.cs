using ScanTrackNode.Graph;
using ScanTrackNode.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// IHttpClientFactory — används av NodeRegistry och PackageForwarder
builder.Services.AddHttpClient();

// Ladda stadsgrafen — hämta från registret om möjligt, annars lokalt
var registryUrl = builder.Configuration["REGISTRY_URL"] ?? "http://localhost:9000";
var csvPath = Path.Combine(AppContext.BaseDirectory, "data", "cities.csv");
var graph = await GraphLoader.LoadFromRegistryOrFileAsync(registryUrl, csvPath);
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
