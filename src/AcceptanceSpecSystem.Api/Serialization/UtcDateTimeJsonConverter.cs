using System.Text.Json;
using System.Text.Json.Serialization;

namespace AcceptanceSpecSystem.Api.Serialization;

/// <summary>
/// 将服务端统一按 UTC 保存的时间输出为带 Z 的 ISO 8601 字符串。
/// MySQL datetime 读回后 DateTime.Kind 会变为 Unspecified，若直接序列化，
/// 浏览器会把它误当成本地时间，导致界面少显示一个时区偏移量。
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => reader.GetDateTime();

    public override void Write(
        Utf8JsonWriter writer,
        DateTime value,
        JsonSerializerOptions options)
    {
        var utcValue = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        writer.WriteStringValue(utcValue);
    }
}
