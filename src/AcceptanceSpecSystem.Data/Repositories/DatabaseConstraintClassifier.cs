using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 按数据库提供程序的稳定错误码识别约束异常。
/// </summary>
public static class DatabaseConstraintClassifier
{
    private const string KeyClauseMarker = " for key ";
    private const int SqliteConstraintErrorCode = 19;
    private const int SqliteConstraintForeignKey = 787;
    private const int SqliteConstraintUnique = 2067;

    /// <summary>
    /// 判断更新失败是否由 MySQL 重复键错误引起。
    /// </summary>
    public static bool IsUniqueViolation(DbUpdateException exception)
    {
        return FindMySqlException(exception)?.ErrorCode == MySqlErrorCode.DuplicateKeyEntry ||
               IsSqliteConstraint(exception, SqliteConstraintUnique);
    }

    /// <summary>
    /// 判断更新失败是否由指定 MySQL 唯一索引的重复键错误引起。
    /// </summary>
    public static bool IsUniqueViolation(DbUpdateException exception, string indexName)
    {
        var providerException = FindMySqlException(exception);
        if (providerException?.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return !string.IsNullOrEmpty(indexName) &&
                   TryReadDuplicateKeyName(providerException.Message, out var duplicateKeyName) &&
                   string.Equals(duplicateKeyName, indexName, StringComparison.Ordinal);
        }

        // SQLite 不暴露索引名；调用方以当前写入目标限定索引，本层只按稳定扩展错误码裁决。
        return !string.IsNullOrEmpty(indexName) &&
               IsSqliteConstraint(exception, SqliteConstraintUnique);
    }

    /// <summary>
    /// 判断删除失败是否为已知的并发或父项外键冲突。
    /// </summary>
    public static bool IsDeleteConflict(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
            return true;

        var providerException = FindMySqlException(exception);
        if (providerException != null)
        {
            return (int)providerException.ErrorCode is 1451 or 1217;
        }

        return IsSqliteConstraint(exception, SqliteConstraintForeignKey);
    }

    private static bool TryReadDuplicateKeyName(string message, out string duplicateKeyName)
    {
        duplicateKeyName = string.Empty;
        var markerIndex = message.LastIndexOf(KeyClauseMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var keyToken = message
            .AsSpan(markerIndex + KeyClauseMarker.Length)
            .Trim();
        return TryReadQuotedIdentifier(keyToken, out duplicateKeyName);
    }

    private static bool TryReadQuotedIdentifier(
        ReadOnlySpan<char> token,
        out string identifierName)
    {
        identifierName = string.Empty;
        var position = 0;
        while (position < token.Length)
        {
            var quote = token[position];
            if (quote is not ('\'' or '"' or '`'))
            {
                return false;
            }

            var quotedContent = token[(position + 1)..];
            var closingQuoteOffset = quotedContent.IndexOf(quote);
            if (closingQuoteOffset < 0)
            {
                return false;
            }

            var identifier = quotedContent[..closingQuoteOffset];
            if (!TryGetLastQualifiedSegment(identifier, out identifierName))
            {
                return false;
            }

            position += closingQuoteOffset + 2;
            if (position == token.Length)
            {
                return true;
            }

            if (token[position] != '.')
            {
                return false;
            }

            position++;
        }

        return false;
    }

    private static bool TryGetLastQualifiedSegment(
        ReadOnlySpan<char> identifier,
        out string identifierName)
    {
        identifierName = string.Empty;
        var lastSeparator = identifier.LastIndexOf('.');
        var lastSegment = identifier[(lastSeparator + 1)..];
        if (lastSegment.IsEmpty ||
            lastSegment.IndexOfAny('\'', '"', '`') >= 0)
        {
            return false;
        }

        identifierName = lastSegment.ToString();
        return true;
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

    private static bool IsSqliteConstraint(Exception exception, int expectedExtendedErrorCode)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            var type = current.GetType();
            if (!string.Equals(
                    type.FullName,
                    "Microsoft.Data.Sqlite.SqliteException",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var errorCode = type.GetProperty("SqliteErrorCode")?.GetValue(current) as int?;
            var extendedErrorCode = type.GetProperty("SqliteExtendedErrorCode")?.GetValue(current) as int?;
            return errorCode == SqliteConstraintErrorCode &&
                   extendedErrorCode == expectedExtendedErrorCode;
        }

        return false;
    }
}
