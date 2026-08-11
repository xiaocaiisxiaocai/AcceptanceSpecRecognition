using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

public sealed class AcceptanceSpecContentVersionRepository(AppDbContext context) :
    Repository<AcceptanceSpecContentVersion>(context),
    IAcceptanceSpecContentVersionRepository
{
}
