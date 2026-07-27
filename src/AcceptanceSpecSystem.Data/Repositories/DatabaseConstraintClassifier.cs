using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 按数据库提供程序的稳定错误码识别约束异常。
/// </summary>
public static class DatabaseConstraintClassifier
{
    private const string KeyClauseMarker = " for key ";

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
               !string.IsNullOrEmpty(indexName) &&
               TryReadDuplicateKeyName(providerException.Message, out var duplicateKeyName) &&
               string.Equals(duplicateKeyName, indexName, StringComparison.Ordinal);
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
}
