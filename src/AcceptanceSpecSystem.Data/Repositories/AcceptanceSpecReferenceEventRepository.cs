using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

public sealed class AcceptanceSpecReferenceEventRepository(
    AppDbContext context) :
    Repository<AcceptanceSpecReferenceEvent>(context),
    IAcceptanceSpecReferenceEventRepository
{
}
