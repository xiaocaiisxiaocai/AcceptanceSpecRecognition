using AcceptanceSpecSystem.Core.Documents.Intelligence;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Strategies;
using AcceptanceSpecSystem.Core.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Core.Tests;

public class DocumentIntelligenceServiceTests
{
    [Fact]
    public async Task IdentifyTargetTableAsync_ShouldPreferTableWithSharedStructureKeywords()
    {
        var service = CreateService();
        var result = await service.IdentifyTargetTableAsync(
        [
            new TableInfo
            {
                Index = 0,
                Name = "说明",
                RowCount = 80,
                ColumnCount = 2,
                Headers = ["序号", "说明"]
            },
            new TableInfo
            {
                Index = 1,
                Name = "验收规格",
                RowCount = 12,
                ColumnCount = 4,
                Headers = ["项目", "规格", "验收标准", "备注"]
            }
        ]);

        result.TableIndex.Should().Be(1);
        result.Confidence.Should().BeGreaterThan(0.8);
    }

    [Fact]
    public void DetectHeaderRowIndex_ShouldPreferHeaderKeywordsOverLeadingDescriptionRows()
    {
        var service = CreateService();
        var table = new TableData
        {
            Rows =
            {
                CreateRow(0, "客户验收规格确认表"),
                CreateRow(1, "项目", "规格", "验收标准", "备注"),
                CreateRow(2, "外观", "无明显划伤", "目视检查", "")
            }
        };

        service.DetectHeaderRowIndex(table).Should().Be(1);
    }

    [Fact]
    public void DetectHeaderRowIndex_ShouldRecognizeCustomerDomainHeaderWords()
    {
        var service = CreateService();
        var table = new TableData
        {
            Rows =
            {
                CreateRow(0, "客户A", "机种X", "版本B", "量产"),
                CreateRow(1, "检查对象", "管制条件", "供应商确认", "补充说明"),
                CreateRow(2, "外观", "表面不得有明显划伤", "OK", "抽检")
            }
        };

        service.DetectHeaderRowIndex(table).Should().Be(1);
    }

    private static DocumentIntelligenceService CreateService()
    {
        return new DocumentIntelligenceService(
            new EmptyRuleBasedMappingStrategy(),
            NullLogger<DocumentIntelligenceService>.Instance);
    }

    private static RowData CreateRow(int rowIndex, params string[] values)
    {
        return new RowData
        {
            Index = rowIndex,
            Cells = values
                .Select((value, columnIndex) => new CellData
                {
                    RowIndex = rowIndex,
                    ColumnIndex = columnIndex,
                    Value = value
                })
                .ToList()
        };
    }

    private sealed class EmptyRuleBasedMappingStrategy : IRuleBasedMappingStrategy
    {
        public Task<ColumnMappingResult> IdentifyAsync(
            IReadOnlyList<string> headers,
            IReadOnlyList<IReadOnlyList<string>> sampleRows,
            IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ColumnMappingResult());
        }
    }
}
