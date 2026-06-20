using ElimsInsightAssistant.Api.Services;

namespace ElimsInsightAssistant.Tests;

// ContractRetriever narrows the registry sent to LLM prompts as the catalog of
// registered services grows. PlanValidator always re-checks against the full
// registry, so these tests only need to cover the narrowing heuristic itself.
public class ContractRetrieverTests
{
    [Fact]
    public void Select_AtOrBelowThreshold_ReturnsEntireRegistryUnchanged()
    {
        var registry = new InMemoryServiceRegistry();
        var all = registry.GetAll();

        var selected = ContractRetriever.Select("anything at all", all);

        Assert.Equal(all.Select(c => c.Name), selected.Select(c => c.Name));
    }

    private static List<ServiceContractEntry> SevenContracts() =>
    [
        new("required-a", "Required A", "actionA", ["fieldA"], "core purpose", "desc", IsRequired: true),
        new("required-b", "Required B", "actionB", ["fieldB"], "core purpose", "desc", IsRequired: true),
        new("widget-service", "Widget Service", "listWidgets", ["widgetId"], "Tracks zorblaxxxx items", "desc", IsRequired: false),
        new("gadget-service", "Gadget Service", "listGadgets", ["gadgetId"], "Tracks fluuxennn items", "desc", IsRequired: false),
        new("gizmo-service", "Gizmo Service", "listGizmos", ["gizmoId"], "Tracks quobnarrr items", "desc", IsRequired: false),
        new("doohickey-service", "Doohickey Service", "listDoohickeys", ["doohickeyId"], "Tracks vexilarrr items", "desc", IsRequired: false),
        new("thingamajig-service", "Thingamajig Service", "listThingamajigs", ["thingamajigId"], "Tracks plombazzz items", "desc", IsRequired: false),
    ];

    [Fact]
    public void Select_AboveThreshold_KeepsRequiredAndKeywordMatchedContracts()
    {
        var all = SevenContracts();

        var selected = ContractRetriever.Select("tell me about widget-service please", all);
        var names = selected.Select(c => c.Name).ToHashSet();

        Assert.Contains("required-a", names);
        Assert.Contains("required-b", names);
        Assert.Contains("widget-service", names);
        Assert.DoesNotContain("gadget-service", names);
        Assert.DoesNotContain("gizmo-service", names);
        Assert.DoesNotContain("doohickey-service", names);
        Assert.DoesNotContain("thingamajig-service", names);
    }

    [Fact]
    public void Select_AboveThreshold_FallsBackToRequiredContracts_WhenNothingMatches()
    {
        var all = SevenContracts();

        var selected = ContractRetriever.Select("completely unrelated gibberish query", all);
        var names = selected.Select(c => c.Name).ToHashSet();

        Assert.Equal(new HashSet<string> { "required-a", "required-b" }, names);
    }
}
