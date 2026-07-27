using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 按数据库提供程序的稳定错误码识别约束异常。
/// </summary>
public static class DatabaseConstraintClassifier
{
    /// <summary>
    /// 判断更新失败是否由 MySQL 重复键错误引起。
    /// </summary>
    public static bool IsUniqueViolation(DbUpdateException exception)
    {
        return FindMySqlException(exception)?.ErrorCode == MySqlErrorCode.DuplicateKeyEntry;
    }

    /// <summary>
    /// 判断更新失败是否由指定 MySQL 唯一索引的重复键错误引起。
    /// </summary>
    public static bool IsUniqueViolation(DbUpdateException exception, string indexName)
    {
        var providerException = FindMySqlException(exception);
        return providerException?.ErrorCode == MySqlErrorCode.DuplicateKeyEntry &&
               providerException.Message.Contains(indexName, StringComparison.Ordinal);
    }

    private static MySqlException? FindMySqlException(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is MySqlException providerException)
            {
                return providerException;
            }
        }

        return null;
    }
}
