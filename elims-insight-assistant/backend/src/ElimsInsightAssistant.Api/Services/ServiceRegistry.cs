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
            Fields: ["studyId", "studyCode", "customer", "legalEntity", "plannedCompletionDate",
                     "assayType", "priority", "studyStatus", "labSite", "studyOwner"],
            Purpose: "Provides study identity, customer, legal entity (eLIMS business/legal operating scope), assay type, priority, status, and planned completion dates",
            Description: "Core study catalogue — identity, customer assignment, legal entity, assay type, priority, status, and planned completion timeline",
            IsRequired: true,
            FieldExamples: new Dictionary<string, IReadOnlyList<string>>
            {
                // Two distinct identifier shapes — the planner uses the pattern to decide
                // which field a user-supplied identifier (e.g. "ST-006" vs "S6") belongs to.
                ["studyId"]      = ["S-BIO-001", "S-DMPK-004", "S-VITRO-006"],
                ["studyCode"]    = ["BIO-PK-001", "DMPK-ADME-004", "VITRO-HTRF-006"],
                ["legalEntity"]  = ["DS-BIOANALYTICS", "DS-DMPK", "DS-IN-VITRO", "DS-TOX"],
                ["assayType"]    = ["PK", "ADME", "IC50", "HTRF", "BIND"],
                ["priority"]     = ["Critical", "High", "Normal"],
                ["studyStatus"]  = ["Completed", "In Progress", "On Hold", "Cancelled"]
            }));

        Register(new ServiceContractEntry(
            Name: "corelabs-service",
            DisplayName: "CoreLabs Service",
            Action: "listTestPs",
            Fields: ["testpId", "studyId", "status", "completedAt", "runType", "result",
                     "qcStatus", "instrument", "failureReason"],
            Purpose: "Provides TestP execution records — status, run type, result, QC status, instrument, failure reason, and actual completion timestamps",
            Description: "TestP execution records — actual completion timestamps are derived from the maximum TestP.completedAt per study",
            IsRequired: true,
            FieldExamples: new Dictionary<string, IReadOnlyList<string>>
            {
                ["status"]    = ["Completed", "Pending", "In Progress"],
                ["result"]    = ["Pass", "Fail"],
                ["runType"]   = ["Production", "Repeat"],
                ["qcStatus"]  = ["Passed", "Failed"],
                ["instrument"] = ["LCMS-01", "LCMS-02", "HTRF-Reader-01"]
            }));

        Register(new ServiceContractEntry(
            Name: "sample-service",
            DisplayName: "Sample Service",
            Action: "listSamples",
            Fields: ["sampleId", "studyId", "sampleType", "status", "collectedAt", "collectionSite", "receivedAt"],
            Purpose: "Provides bioanalytical sample records — sample type, collection site, status, collection and receipt timestamps",
            Description: "Bioanalytical sample catalogue — collection/receipt timestamps, sample type, status, and site linkage",
            IsRequired: false,
            FieldExamples: new Dictionary<string, IReadOnlyList<string>>
            {
                ["status"] = ["Received", "Expected", "Missing"]
            }));

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
