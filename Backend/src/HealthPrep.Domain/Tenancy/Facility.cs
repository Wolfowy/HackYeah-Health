namespace HealthPrep.Domain.Tenancy;

public sealed class Facility
{
    private Facility() { }
    public Facility(string name, string apiKeyHash) { Id = Guid.NewGuid(); Name = name; ApiKeyHash = apiKeyHash; }
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string ApiKeyHash { get; private set; } = "";
}
