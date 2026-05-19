using System.Collections.Concurrent;

namespace ElimsInsightAssistant.Api.Services;

public record ServiceContractEntry(
    string Name,
    string DisplayName,
    string Action,
    IReadOnlyList<string> Fields,
    string Purpose,       // injected into AI prompt
    string Description,   // shown in UI
    bool IsRequired = true
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
            IsRequired: true));

        Register(new ServiceContractEntry(
            Name: "corelabs-service",
            DisplayName: "CoreLabs Service",
            Action: "listTestPs",
            Fields: ["testpId", "studyId", "status", "completedAt", "runType", "result"],
            Purpose: "Provides TestP execution records — status, run type, result, and actual completion timestamps",
            Description: "TestP execution records — actual completion timestamps are derived from the maximum TestP.completedAt per study",
            IsRequired: true));

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
            IsRequired: false));
    }

    public IReadOnlyList<ServiceContractEntry> GetAll() =>
        [.. _contracts.Values.OrderBy(c => c.Name)];

    public ServiceContractEntry? Get(string name) =>
        _contracts.TryGetValue(name, out var e) ? e : null;

    public void Register(ServiceContractEntry entry) =>
        _contracts.AddOrUpdate(entry.Name, entry, (_, _) => entry);

}
