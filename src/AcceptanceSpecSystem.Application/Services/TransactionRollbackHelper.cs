using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Application.Services;

internal static class TransactionRollbackHelper
{
    public static async Task TryRollbackAsync(IUnitOfWork unitOfWork)
    {
        try
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
        }
        catch
        {
            // 回滚是异常处理的补偿动作，不得覆盖原始数据库失败或请求取消。
        }
    }
}
