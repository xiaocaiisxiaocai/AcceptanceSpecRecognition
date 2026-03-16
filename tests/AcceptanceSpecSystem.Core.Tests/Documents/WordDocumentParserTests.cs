using AcceptanceSpecSystem.Core.Documents.Parsers;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Tests.Helpers;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests.Documents;

/// <summary>
/// WordDocumentParser测试
/// </summary>
public class WordDocumentParserTests
{
    private readonly WordDocumentParser _parser = new();

    [Fact]
    public void CanParse_ShouldReturnTrue_ForDocxFile()
    {
        // Act
        var result = _parser.CanParse("test.docx");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanParse_ShouldReturnFalse_ForNonDocxFile()
    {
        // Act & Assert
        _parser.CanParse("test.doc").Should().BeFalse();
        _parser.CanParse("test.xlsx").Should().BeFalse();
        _parser.CanParse("test.txt").Should().BeFalse();
        _parser.CanParse("").Should().BeFalse();
    }

    [Fact]
    public async Task GetTablesAsync_ShouldReturnTableInfo_ForSimpleTable()
    {
        // Arrange
        var data = new string[,]
        {
            { "项目", "规格", "验收", "备注" },
            { "项目1", "规格1", "OK", "备注1" },
            { "项目2", "规格2", "NG", "备注2" }
        };
        using var stream = TestWordDocumentHelper.CreateSimpleTableDocument(data);

        // Act
        var tables = await _parser.GetTablesAsync(stream);

        // Assert
        tables.Should().HaveCount(1);
        tables[0].Index.Should().Be(0);
        tables[0].RowCount.Should().Be(3);
        tables[0].ColumnCount.Should().Be(4);
        tables[0].Headers.Should().Contain("项目", "规格", "验收", "备注");
    }

    [Fact]
    public async Task GetTablesAsync_ShouldReturnMultipleTables()
    {
        // Arrange
        var table1 = new string[,]
        {
            { "A1", "B1" },
            { "A2", "B2" }
        };
        var table2 = new string[,]
        {
            { "X1", "Y1", "Z1" },
            { "X2", "Y2", "Z2" }
        };
        using var stream = TestWordDocumentHelper.CreateMultiTableDocument(table1, table2);

        // Act
        var tables = await _parser.GetTablesAsync(stream);

        // Assert
        tables.Should().HaveCount(2);
        tables[0].ColumnCount.Should().Be(2);
        tables[1].ColumnCount.Should().Be(3);
    }

    [Fact]
    public async Task GetTablesAsync_ShouldIgnoreNestedTables_WhenAssigningIndexes()
    {
        // Arrange
        using var stream = TestWordDocumentHelper.CreateDocumentWithNestedAndMultipleTopLevelTables();

        // Act
        var tables = await _parser.GetTablesAsync(stream);

        // Assert
        tables.Should().HaveCount(2);
        tables[0].Headers.Should().Contain("外层表1-列1", "外层表1-列2");
        tables[1].Headers.Should().Contain("目标表-项目", "目标表-规格", "目标表-验收", "目标表-备注");
    }

    [Fact]
    public async Task ExtractTableDataAsync_ShouldExtractAllData()
    {
        // Arrange
        var data = new string[,]
        {
            { "项目", "规格", "验收", "备注" },
            { "项目1", "规格1", "OK", "备注1" },
            { "项目2", "规格2", "NG", "备注2" }
        };
        using var stream = TestWordDocumentHelper.CreateSimpleTableDocument(data);

        // Act
        var tableData = await _parser.ExtractTableDataAsync(stream, 0);

        // Assert
        tableData.Headers.Should().HaveCount(4);
        tableData.Headers[0].Should().Be("项目");
        tableData.Headers[1].Should().Be("规格");
        tableData.Headers[2].Should().Be("验收");
        tableData.Headers[3].Should().Be("备注");

        tableData.Rows.Should().HaveCount(2);
        tableData.Rows[0].GetValue(0).Should().Be("项目1");
        tableData.Rows[0].GetValue(1).Should().Be("规格1");
        tableData.Rows[0].GetValue(2).Should().Be("OK");
        tableData.Rows[0].GetValue(3).Should().Be("备注1");
    }

    [Fact]
    public async Task ExtractTableDataAsync_WithColumnMapping_ShouldRespectMapping()
    {
        // Arrange
        var data = new string[,]
        {
            { "表头行0" },
            { "真正的表头" },
            { "数据行1" },
            { "数据行2" }
        };
        using var stream = TestWordDocumentHelper.CreateSimpleTableDocument(data);

        var mapping = new ColumnMapping
        {
            HeaderRowIndex = 1,
            DataStartRowIndex = 2
        };

        // Act
        var tableData = await _parser.ExtractTableDataAsync(stream, 0, mapping);

        // Assert
        tableData.Headers[0].Should().Be("真正的表头");
        tableData.Rows.Should().HaveCount(2);
        tableData.Rows[0].GetValue(0).Should().Be("数据行1");
        tableData.Rows[1].GetValue(0).Should().Be("数据行2");
    }

    [Fact]
    public async Task ExtractTableDataAsync_ShouldHandleHorizontalMergedCells()
    {
        // Arrange
        using var stream = TestWordDocumentHelper.CreateHorizontalMergedTableDocument();

        // Act
        var tables = await _parser.GetTablesAsync(stream);
        stream.Position = 0;
        var tableData = await _parser.ExtractTableDataAsync(stream, 0);

        // Assert
        tables[0].HasMergedCells.Should().BeTrue();
        tableData.Headers.Should().Contain("A1", "B1", "C1", "D1");

        // 第二行的合并单元格应该被拆分，填充相同内容
        tableData.Rows[0].Cells.Should().Contain(c => c.Value == "A2-B2");
    }

    [Fact]
    public async Task ExtractTableDataAsync_ShouldHandleVerticalMergedCells()
    {
        // Arrange
        using var stream = TestWordDocumentHelper.CreateVerticalMergedTableDocument();

        // Act
        var tables = await _parser.GetTablesAsync(stream);
        stream.Position = 0;
        var tableData = await _parser.ExtractTableDataAsync(stream, 0);

        // Assert
        tables[0].HasMergedCells.Should().BeTrue();
        tableData.MergedCells.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExtractTableDataAsync_ShouldThrow_ForInvalidTableIndex()
    {
        // Arrange
        var data = new string[,] { { "A1" } };
        using var stream = TestWordDocumentHelper.CreateSimpleTableDocument(data);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _parser.ExtractTableDataAsync(stream, 5));
    }

    [Fact]
    public async Task ExtractAllTablesDataAsync_ShouldExtractAllTables()
    {
        // Arrange
        var table1 = new string[,]
        {
            { "表1-A1", "表1-B1" },
            { "表1-A2", "表1-B2" }
        };
        var table2 = new string[,]
        {
            { "表2-X1" },
            { "表2-X2" }
        };
        using var stream = TestWordDocumentHelper.CreateMultiTableDocument(table1, table2);

        // Act
        var allTables = await _parser.ExtractAllTablesDataAsync(stream);

        // Assert
        allTables.Should().HaveCount(2);
        allTables[0].Headers.Should().Contain("表1-A1", "表1-B1");
        allTables[1].Headers.Should().Contain("表2-X1");
    }

    [Fact]
    public void DocumentType_ShouldBeWord()
    {
        // Assert
        _parser.DocumentType.Should().Be(DocumentType.Word);
    }
}
