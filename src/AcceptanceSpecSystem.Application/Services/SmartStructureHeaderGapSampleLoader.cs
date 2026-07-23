using System.Text.Json;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Services;

public static class SmartStructureHeaderGapSampleLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<DocumentTemplate> LoadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var samples = JsonSerializer.Deserialize<List<SmartStructureHeaderGapSample>>(json, JsonOptions) ?? [];
        var templates = new List<DocumentTemplate>();
        var nextId = 1;
        foreach (var sample in samples)
        {
            if (sample.Headers.Count == 0)
            {
                continue;
            }

            templates.Add(new DocumentTemplate
            {
                Id = nextId++,
                CustomerId = sample.CustomerId,
                TemplateName = string.IsNullOrWhiteSpace(sample.TemplateName)
                    ? $"离线样本-{nextId - 1}"
                    : sample.TemplateName.Trim(),
                HeadersJson = JsonSerializer.Serialize(sample.Headers),
                ProjectColumnIndex = sample.IsSpecificationOnly ? null : NormalizeOptionalIndex(sample.ProjectColumnIndex),
                SpecificationColumnIndex = NormalizeRequiredIndex(sample.SpecificationColumnIndex),
                AcceptanceColumnIndex = NormalizeOptionalIndex(sample.AcceptanceColumnIndex),
                RemarkColumnIndex = NormalizeOptionalIndex(sample.RemarkColumnIndex),
                HeaderRowIndex = Math.Max(0, sample.HeaderRowIndex),
                HeaderRowCount = Math.Max(1, sample.HeaderRowCount),
                DataStartRowIndex = Math.Max(1, sample.DataStartRowIndex),
                IsSpecificationOnly = sample.IsSpecificationOnly,
                ConfirmedAt = sample.ConfirmedAt ?? DateTime.UtcNow,
                CreatedAt = sample.ConfirmedAt ?? DateTime.UtcNow,
                UpdatedAt = sample.ConfirmedAt ?? DateTime.UtcNow
            });
        }

        return templates;
    }

    private static int NormalizeRequiredIndex(int? value) => value.GetValueOrDefault(-1);

    private static int? NormalizeOptionalIndex(int? value) => value.HasValue && value.Value >= 0 ? value.Value : null;
}

public sealed class SmartStructureHeaderGapSample
{
    public int CustomerId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public List<string> Headers { get; set; } = [];
    public int? ProjectColumnIndex { get; set; }
    public int? SpecificationColumnIndex { get; set; }
    public int? AcceptanceColumnIndex { get; set; }
    public int? RemarkColumnIndex { get; set; }
    public int HeaderRowIndex { get; set; }
    public int HeaderRowCount { get; set; } = 1;
    public int DataStartRowIndex { get; set; } = 1;
    public bool IsSpecificationOnly { get; set; }
    public DateTime? ConfirmedAt { get; set; }
}
