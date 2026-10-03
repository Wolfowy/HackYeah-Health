namespace DocPrep.Domain.Tenancy;

public enum FacilityRole { Administrative, Clinician, System }
public sealed class Facility
{
    private Facility() { }
    public Facility(Guid id, string name) { Id = id; Name = name; }
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
}
