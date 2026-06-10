using System.Security.Cryptography;
using System.Text;

namespace AcceptanceSpecSystem.Core.Diagnostics;

/// <summary>
/// 敏感日志格式化：保留排查所需的长度和摘要，避免把业务原文写入日志或审计。
/// </summary>
public static class SensitiveLogFormatter
{
    private static readonly string[] SensitiveMarkers =
    [
        "password",
        "passwd",
        "pwd=",
        "secret",
        "token",
        "apikey",
        "api-key",
        "api key",
        "authorization",
        "bearer ",
        "server=",
        "host=",
        "user id",
        "data source",
        "http://",
        "https://",
        "stack trace",
        "exception"
    ];

    public static string DescribePayload(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "length=0, sha256=empty";
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return $"length={value.Length}, sha256={hash[..16]}";
    }

    public static string SanitizeMessage(string? message, string fallback = "已脱敏")
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return fallback;
        }

        var sanitized = message.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return fallback;
        }

        if (SensitiveMarkers.Any(marker => sanitized.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return fallback;
        }

        return sanitized.Length <= 120 ? sanitized : fallback;
    }
}
