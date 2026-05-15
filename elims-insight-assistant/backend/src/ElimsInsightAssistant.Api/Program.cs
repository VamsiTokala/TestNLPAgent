using ElimsInsightAssistant.Api.Audit;
using ElimsInsightAssistant.Api.Execution;
using ElimsInsightAssistant.Api.Services;
using ElimsInsightAssistant.Api.Validation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var openAiKey = builder.Configuration["OpenAI:ApiKey"];
if (!string.IsNullOrWhiteSpace(openAiKey))
    builder.Services.AddSingleton<IPlanGenerator, OpenAiPlanGenerator>();
else
    builder.Services.AddSingleton<IPlanGenerator, MockPlanGenerator>();

builder.Services.AddSingleton<IPlanValidator, PlanValidator>();
builder.Services.AddSingleton<IAuditService, InMemoryAuditService>();
builder.Services.AddScoped<IStudyServiceClient, DemoStudyServiceClient>();
builder.Services.AddScoped<ICoreLabsServiceClient, DemoCoreLabsServiceClient>();
builder.Services.AddScoped<IClassificationService, StudyCompletionClassificationService>();
builder.Services.AddScoped<IExecutionEngine, ExecutionEngine>();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();

// Log active mode clearly at startup so there is no silent fallback to mock
var logger = app.Services.GetRequiredService<ILogger<Program>>();
if (!string.IsNullOrWhiteSpace(openAiKey))
    logger.LogInformation("Plan generator: OpenAiPlanGenerator (gpt-4o-mini, structured outputs)");
else
    logger.LogWarning(
        "Plan generator: MockPlanGenerator (keyword matching only — no real NLP). " +
        "To enable real NL intent extraction set OpenAI:ApiKey. " +
        "See docs/build-from-scratch.md §14.1 for how to obtain and configure a key.");

app.Run();
