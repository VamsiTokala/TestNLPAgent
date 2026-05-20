using System.Collections.Concurrent;

namespace ElimsInsightAssistant.Api.Services;

public record ServiceContractEntry(
    string Name,
    string DisplayName,
    string Action,
    IReadOnlyList<string> Fields,
    string Purpose,       // injected into AI prompt
    string Description,   // shown in UI
    bool IsRequired = true,
    // Optional per-field vocabulary so the planner uses literal values that exist
    // in the data (e.g. {"result": ["Pass","Fail"], "status": ["Completed","Pending"]}).
    IReadOnlyDictionary<string, IReadOnlyList<string>>? FieldExamples = null
);

public interface IServiceRegistry
{
    IReadOnlyList<ServiceContractEntry> GetAll();
    ServiceContractEntry? Get(string name);
    void Register(ServiceContractEntry entry);
}

public class InMemoryServiceRegistry : IServiceRegistry
{
    private readonly ConcurrentDictionary<string, ServiceContractEntry> _contracts;

    public InMemoryServiceRegistry()
    {
        _contracts = new(StringComparer.OrdinalIgnoreCase);

        Register(new ServiceContractEntry(
            Name: "study-service",
            DisplayName: "Study Service",
            Action: "listStudies",
            Fields: ["studyId", "studyCode", "customer", "legalEntity", "plannedCompletionDate"],
            Purpose: "Provides study identity, customer, legal entity, and planned completion dates",
            Description: "Core study catalogue — identity, customer assignment, legal entity, and planned completion timeline",
            IsRequired: true,
            FieldExamples: new Dictionary<string, IReadOnlyList<string>>
            {
                // Two distinct identifier shapes — the planner uses the pattern to decide
                // which field a user-supplied identifier (e.g. "ST-006" vs "S6") belongs to.
                ["studyId"]   = ["S1", "S2", "S3"],
                ["studyCode"] = ["ST-001", "ST-002", "ST-003"]
            }));

        Register(new ServiceContractEntry(
            Name: "corelabs-service",
            DisplayName: "CoreLabs Service",
            Action: "listTestPs",
            Fields: ["testpId", "studyId", "status", "completedAt", "runType", "result"],
            Purpose: "Provides TestP execution records — status, run type, result, and actual completion timestamps",
            Description: "TestP execution records — actual completion timestamps are derived from the maximum TestP.completedAt per study",
            IsRequired: true,
            FieldExamples: new Dictionary<string, IReadOnlyList<string>>
            {
                ["status"]  = ["Completed", "Pending"],
                ["result"]  = ["Pass", "Fail"],
                ["runType"] = ["Production", "Repeat"]
            }));

        Register(new ServiceContractEntry(
            Name: "sample-service",
            DisplayName: "Sample Service",
            Action: "listSamples",
            Fields: ["sampleId", "studyId", "sampleType", "status", "collectedAt", "collectionSite"],
            Purpose: "Provides bioanalytical sample records — sample type, collection site, status, and collection timestamps",
            Description: "Bioanalytical sample catalogue — collection timestamps, sample type, status, and site linkage",
            IsRequired: false));

        Register(new ServiceContractEntry(
            Name: "protocol-service",
            DisplayName: "Protocol Service",
            Action: "listProtocols",
            Fields: ["protocolId", "studyId", "version", "status", "approvedAt", "expiresAt"],
            Purpose: "Provides study protocol records — protocol version, approval status, and expiry dates",
            Description: "Study protocol catalogue — approved versions, status, and expiry timeline",
            IsRequired: false,
            FieldExamples: new Dictionary<string, IReadOnlyList<string>>
            {
                ["status"] = ["Approved", "Draft", "Expired"]
            }));
    }

    public IReadOnlyList<ServiceContractEntry> GetAll() =>
        [.. _contracts.Values.OrderBy(c => c.Name)];

    public ServiceContractEntry? Get(string name) =>
        _contracts.TryGetValue(name, out var e) ? e : null;

    public void Register(ServiceContractEntry entry) =>
        _contracts.AddOrUpdate(entry.Name, entry, (_, _) => entry);

}
