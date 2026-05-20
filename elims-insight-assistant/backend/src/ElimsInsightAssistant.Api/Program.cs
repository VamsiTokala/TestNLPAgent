using ElimsInsightAssistant.Api.Audit;
using ElimsInsightAssistant.Api.Execution;
using ElimsInsightAssistant.Api.Services;
using ElimsInsightAssistant.Api.Validation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<IServiceRegistry, InMemoryServiceRegistry>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var geminiKey     = builder.Configuration["Gemini:ApiKey"];
var openRouterKey = builder.Configuration["OpenRouter:ApiKey"];

Console.WriteLine($"[CONFIG] Gemini:ApiKey      = {(string.IsNullOrWhiteSpace(geminiKey)     ? "NOT SET" : $"set ({geminiKey!.Length} chars)")}");
Console.WriteLine($"[CONFIG] OpenRouter:ApiKey  = {(string.IsNullOrWhiteSpace(openRouterKey) ? "NOT SET" : $"set ({openRouterKey!.Length} chars)")}");

// Register every available generator — controller picks one per request
builder.Services.AddSingleton<MockPlanGenerator>();
if (!string.IsNullOrWhiteSpace(geminiKey))     builder.Services.AddSingleton<GeminiPlanGenerator>();
if (!string.IsNullOrWhiteSpace(openRouterKey)) builder.Services.AddSingleton<OpenRouterPlanGenerator>();

// Default generator (used when no provider is specified in the request)
if (!string.IsNullOrWhiteSpace(geminiKey))
    builder.Services.AddSingleton<IPlanGenerator, GeminiPlanGenerator>();
else if (!string.IsNullOrWhiteSpace(openRouterKey))
    builder.Services.AddSingleton<IPlanGenerator, OpenRouterPlanGenerator>();
else
    builder.Services.AddSingleton<IPlanGenerator, MockPlanGenerator>();

builder.Services.AddSingleton<IPlanValidator, PlanValidator>();
builder.Services.AddSingleton<IAuditService, InMemoryAuditService>();
builder.Services.AddScoped<IStudyServiceClient,    DemoStudyServiceClient>();
builder.Services.AddScoped<ICoreLabsServiceClient, DemoCoreLabsServiceClient>();
builder.Services.AddScoped<IProtocolServiceClient, DemoProtocolServiceClient>();
builder.Services.AddScoped<ISampleServiceClient,   DemoSampleServiceClient>();
builder.Services.AddScoped<IExecutionEngine, ExecutionEngine>();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
var available = new List<string> { "mock" };
if (!string.IsNullOrWhiteSpace(geminiKey))     available.Add("gemini");
if (!string.IsNullOrWhiteSpace(openRouterKey)) available.Add("openrouter");
logger.LogInformation("Available plan generators: {Generators}", string.Join(", ", available));

app.Run();
