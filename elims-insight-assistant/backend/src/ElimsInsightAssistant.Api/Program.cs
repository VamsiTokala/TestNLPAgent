using ElimsInsightAssistant.Api.Audit;
using ElimsInsightAssistant.Api.Execution;
using ElimsInsightAssistant.Api.Services;
using ElimsInsightAssistant.Api.Validation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// Use OpenAI when an API key is present; fall back to rule-based mock for local dev
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
app.Run();
