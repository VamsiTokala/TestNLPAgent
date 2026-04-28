# eLIMS Insight Assistant Backend

## Run
`dotnet run --project src/ElimsInsightAssistant.Api`

## Test
`dotnet test src/ElimsInsightAssistant.Tests`

## APIs
- POST /api/assistant/query
- POST /api/assistant/plan
- POST /api/assistant/plan/validate
- POST /api/assistant/execute
- GET /api/assistant/audit/{traceId}
- GET /api/demo/studies
- GET /api/demo/corelabs/testps
