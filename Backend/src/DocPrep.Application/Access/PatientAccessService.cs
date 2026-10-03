using DocPrep.Application.Abstractions;
using DocPrep.Application.Common;
using DocPrep.Application.Contracts;

namespace DocPrep.Application.Access;

public sealed class PatientAccessService(IDocPrepStore store, ICredentialService credentials, IPatientSessionStore sessions, IClock clock)
{
    public async Task<ExchangeAccessResult> ExchangeLink(string token, CancellationToken ct) =>
        await Exchange(await store.FindAccessByLinkHash(credentials.Hash(token), ct), ct);
    public async Task<ExchangeAccessResult> ExchangeCode(string code, CancellationToken ct) =>
        await Exchange(await store.FindAccessByCodeHash(credentials.Hash(code), ct), ct);

    private async Task<ExchangeAccessResult> Exchange(Domain.Access.PatientAccessGrant? grant, CancellationToken ct)
    {
        if (grant is null || !grant.IsValid(clock.UtcNow)) throw new ForbiddenError("Access credential is invalid or expired.");
        var visit = await store.GetVisit(grant.VisitProcessId, ct) ?? throw new ForbiddenError();
        grant.MarkOpened(clock.UtcNow); visit.Open(clock.UtcNow);
        var token = await sessions.Create(visit.Id, visit.ServiceExpiresAt, ct); await store.Save(ct);
        return new(token, visit.Id, visit.ServiceExpiresAt);
    }
}
