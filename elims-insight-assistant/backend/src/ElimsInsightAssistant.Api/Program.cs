using ElimsInsightAssistant.Api.Audit;
using ElimsInsightAssistant.Api.Execution;
using ElimsInsightAssistant.Api.Services;
using ElimsInsightAssistant.Api.Validation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
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
