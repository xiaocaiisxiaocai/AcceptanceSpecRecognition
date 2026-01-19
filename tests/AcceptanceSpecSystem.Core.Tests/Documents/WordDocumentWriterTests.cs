using AcceptanceSpecSystem.Core.Documents.Writers;
using AcceptanceSpecSystem.Core.Documents.Parsers;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Tests.Helpers;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests.Documents;

/// <summary>
/// WordDocumentWriter测试
/// </summary>
public class WordDocumentWriterTests
{
    private readonly WordDocumentWriter _writer = new();
    private readonly WordDocumentParser _parser = new();

    [Fact]
    public void CanWrite_ShouldReturnTrue_ForDocxFile()
    {
        // Act
        var result = _writer.CanWrite("test.docx");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanWrite_ShouldReturnFalse_ForNonDocxFile()
    {
        // Act & Assert
        _writer.CanWrite("test.doc").Should().BeFalse();
        _writer.CanWrite("test.xlsx").Should().BeFalse();
        _writer.CanWrite("").Should().BeFalse();
    }

    [Fact]
    public async Task WriteTableDataAsync_ShouldUpdateCell()
    {
        // Arrange
        var data = new string[,]
        {
            { "项目", "规格", "验收", "备注" },
            { "项目1", "规格1", "", "" },
            { "项目2", "规格2", "", "" }
        };
        using var stream = TestWordDocumentHelper.CreateSimpleTableDocument(data);

        var operations = new List<CellWriteOperation>
        {
            CellWriteOperation.Create(1, 2, "OK"),
            CellWriteOperation.Create(2, 2, "NG")
        };

        // Act
        var count = await _writer.WriteTableDataAsync(stream, 0, operations);

        // Assert
        count.Should().Be(2);

        // 验证写入结果
        stream.Position = 0;
        var tableData = await _parser.ExtractTableDataAsync(stream, 0);
        tableData.Rows[0].GetValue(2).Should().Be("OK");
        tableData.Rows[1].GetValue(2).Should().Be("NG");
    }

    [Fact]
    public async Task WriteTableDataAsync_ShouldReturnZero_ForEmptyOperations()
    {
        // Arrange
        var data = new string[,] { { "A1" } };
        using var stream = TestWordDocumentHelper.CreateSimpleTableDocument(data);

        // Act
        var count = await _writer.WriteTableDataAsync(stream, 0, Array.Empty<CellWriteOperation>());

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public async Task WriteCellAsync_ShouldUpdateSingleCell()
    {
        // Arrange
        var data = new string[,]
        {
            { "原始值" }
        };
        using var stream = TestWordDocumentHelper.CreateSimpleTableDocument(data);

        // Act
        var result = await _writer.WriteCellAsync(stream, 0, 0, 0, "新值");

        // Assert
        result.Should().BeTrue();

        stream.Position = 0;
        var tableData = await _parser.ExtractTableDataAsync(stream, 0);
        tableData.Headers[0].Should().Be("新值");
    }

    [Fact]
    public async Task WriteTableDataAsync_ShouldHandleMergedCells()
    {
        // Arrange
        using var stream = TestWordDocumentHelper.CreateHorizontalMergedTableDocument();

        var operations = new List<CellWriteOperation>
        {
            // 写入合并单元格的第一列位置
            CellWriteOperation.Create(1, 0, "新合并值")
        };

        // Act
        var count = await _writer.WriteTableDataAsync(stream, 0, operations);

        // Assert
        count.Should().Be(1);

        stream.Position = 0;
        var tableData = await _parser.ExtractTableDataAsync(stream, 0);
        // 合并单元格应该被更新
        tableData.Rows[0].Cells.Should().Contain(c => c.Value == "新合并值");
    }

    [Fact]
    public async Task WriteTableDataAsync_ShouldHandleInvalidCellPosition()
    {
        // Arrange
        var data = new string[,] { { "A1", "B1" } };
        using var stream = TestWordDocumentHelper.CreateSimpleTableDocument(data);

        var operations = new List<CellWriteOperation>
        {
            CellWriteOperation.Create(0, 0, "有效"),
            CellWriteOperation.Create(99, 99, "无效位置") // 超出范围的位置
        };

        // Act
        var count = await _writer.WriteTableDataAsync(stream, 0, operations);

        // Assert
        count.Should().Be(1); // 只有一个操作成功
    }

    [Fact]
    public async Task WriteTableDataAsync_ShouldPreserveFormatting()
    {
        // Arrange
        var data = new string[,]
        {
            { "项目", "规格" },
            { "项目1", "规格1" }
        };
        using var stream = TestWordDocumentHelper.CreateSimpleTableDocument(data);

        var operations = new List<CellWriteOperation>
        {
            new CellWriteOperation
            {
                RowIndex = 1,
                ColumnIndex = 0,
                Value = "更新后的项目",
                PreserveFormatting = true
            }
        };

        // Act
        var count = await _writer.WriteTableDataAsync(stream, 0, operations);

        // Assert
        count.Should().Be(1);

        stream.Position = 0;
        var tableData = await _parser.ExtractTableDataAsync(stream, 0);
        tableData.Rows[0].GetValue(0).Should().Be("更新后的项目");
    }

    [Fact]
    public async Task WriteToNewFileAsync_ShouldCreateNewFile()
    {
        // Arrange
        var data = new string[,]
        {
            { "项目", "规格" },
            { "项目1", "规格1" }
        };

        var tempDir = Path.GetTempPath();
        var sourceFile = Path.Combine(tempDir, $"source_{Guid.NewGuid()}.docx");
        var targetFile = Path.Combine(tempDir, $"target_{Guid.NewGuid()}.docx");

        try
        {
            // 创建源文件
            using (var sourceStream = TestWordDocumentHelper.CreateSimpleTableDocument(data))
            {
                using var fileStream = File.Create(sourceFile);
                sourceStream.CopyTo(fileStream);
            }

            var operations = new List<CellWriteOperation>
            {
                CellWriteOperation.Create(1, 1, "更新的规格")
            };

            // Act
            var count = await _writer.WriteToNewFileAsync(sourceFile, targetFile, 0, operations);

            // Assert
            count.Should().Be(1);
            File.Exists(targetFile).Should().BeTrue();

            // 验证源文件未被修改
            using var sourceReadStream = File.OpenRead(sourceFile);
            var sourceData = await _parser.ExtractTableDataAsync(sourceReadStream, 0);
            sourceData.Rows[0].GetValue(1).Should().Be("规格1");

            // 验证目标文件已被修改
            using var targetReadStream = File.OpenRead(targetFile);
            var targetData = await _parser.ExtractTableDataAsync(targetReadStream, 0);
            targetData.Rows[0].GetValue(1).Should().Be("更新的规格");
        }
        finally
        {
            // 清理临时文件
            if (File.Exists(sourceFile)) File.Delete(sourceFile);
            if (File.Exists(targetFile)) File.Delete(targetFile);
        }
    }

    [Fact]
    public void DocumentType_ShouldBeWord()
    {
        // Assert
        _writer.DocumentType.Should().Be(DocumentType.Word);
    }
}
