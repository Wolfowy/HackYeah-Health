using DocPrep.Domain.Common;

namespace DocPrep.Domain.Tenancy;

public sealed class Clinician
{
    private Clinician() { }
    public Clinician(Guid facilityId, string id, string name, string specialty, string? defaultRoom, DateTimeOffset now)
    {
        FacilityId = facilityId; Id = Guard.Required(id, nameof(id), 200); CreatedAt = now;
        Update(name, specialty, defaultRoom, true);
    }
    public Guid FacilityId { get; private set; }
    public string Id { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Specialty { get; private set; } = "";
    public string? DefaultRoom { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public void Update(string name, string specialty, string? defaultRoom, bool isActive)
    {
        Name = Guard.Required(name, nameof(name), 200);
        Specialty = Guard.Required(specialty, nameof(specialty), 200);
        DefaultRoom = string.IsNullOrWhiteSpace(defaultRoom) ? null : Guard.Required(defaultRoom, nameof(defaultRoom), 100);
        IsActive = isActive;
    }
}
