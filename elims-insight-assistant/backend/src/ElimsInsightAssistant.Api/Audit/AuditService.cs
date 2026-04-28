using ElimsInsightAssistant.Api.Models;

namespace ElimsInsightAssistant.Api.Audit;

public interface IAuditService
{
    void Save(AuditRecord record);
    AuditRecord? Get(string traceId);
}

public class InMemoryAuditService : IAuditService
{
    private readonly Dictionary<string, AuditRecord> _records = [];
    public void Save(AuditRecord record) => _records[record.TraceId] = record;
    public AuditRecord? Get(string traceId) => _records.TryGetValue(traceId, out var r) ? r : null;
}
