using AcceptanceSpecSystem.Core.Documents.Interfaces;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Documents.Parsers;
using AcceptanceSpecSystem.Core.Documents.Writers;
using AcceptanceSpecSystem.Core.Tests.Helpers;
using ClosedXML.Excel;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests.Documents;

public sealed class DocumentParserWriterCancellationTests
{
    [Theory]
    [InlineData(DocumentType.Word)]
    [InlineData(DocumentType.Excel)]
    public async Task Parser_ShouldObserveCancellationAfterSynchronousPackageLoad(DocumentType documentType)
    {
        using var source = CreateDocument(documentType);
        using var cancellation = new CancellationTokenSource();
        using var triggeringStream = new CancelOnFirstReadStream(source, cancellation);
        IDocumentParser parser = documentType == DocumentType.Word
            ? new WordDocumentParser()
            : new ExcelDocumentParser();

        Func<Task> parse = async () => await parser.GetTablesAsync(triggeringStream, cancellation.Token);

        await parse.Should().ThrowAsync<OperationCanceledException>();
        cancellation.IsCancellationRequested.Should().BeTrue();
    }

    [Theory]
    [InlineData(DocumentType.Word)]
    [InlineData(DocumentType.Excel)]
    public async Task Writer_ShouldCancelBeforeSaveAndLeaveInputBytesUnchanged(DocumentType documentType)
    {
        using var stream = CreateDocument(documentType);
        var originalBytes = stream.ToArray();
        using var cancellation = new CancellationTokenSource();
        IDocumentWriter writer = documentType == DocumentType.Word
            ? new WordDocumentWriter()
            : new ExcelDocumentWriter();

        Func<Task> write = async () => await writer.WriteTableDataAsync(
            stream,
            0,
            CancelWhileEnumerating(cancellation),
            cancellation.Token);

        await write.Should().ThrowAsync<OperationCanceledException>();
        stream.ToArray().Should().Equal(originalBytes, "取消发生在保存边界前时不得发布部分写入");
    }

    [Theory]
    [InlineData(DocumentType.Word, ".docx")]
    [InlineData(DocumentType.Excel, ".xlsx")]
    public async Task WriteToNewFile_ShouldDeleteTargetWhenCancelledBeforeSave(
        DocumentType documentType,
        string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "AcceptanceSpecSystem.WriterCancellation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, $"source{extension}");
        var targetPath = Path.Combine(root, $"target{extension}");
        try
        {
            using (var source = CreateDocument(documentType))
            {
                await File.WriteAllBytesAsync(sourcePath, source.ToArray());
            }

            using var cancellation = new CancellationTokenSource();
            IDocumentWriter writer = documentType == DocumentType.Word
                ? new WordDocumentWriter()
                : new ExcelDocumentWriter();

            Func<Task> write = async () => await writer.WriteToNewFileAsync(
                sourcePath,
                targetPath,
                0,
                CancelWhileEnumerating(cancellation),
                cancellation.Token);

            await write.Should().ThrowAsync<OperationCanceledException>();
            File.Exists(targetPath).Should().BeFalse("取消后的未完成目标文件不得发布");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static IEnumerable<CellWriteOperation> CancelWhileEnumerating(CancellationTokenSource cancellation)
    {
        yield return CellWriteOperation.Create(1, 0, "不会发布");
        cancellation.Cancel();
        yield return CellWriteOperation.Create(1, 1, "不会发布");
    }

    private static MemoryStream CreateDocument(DocumentType documentType)
    {
        if (documentType == DocumentType.Word)
        {
            return TestWordDocumentHelper.CreateSimpleTableDocument(new[,]
            {
                { "项目", "规格" },
                { "P1", "S1" }
            });
        }

        var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Sheet1");
            sheet.Cell(1, 1).Value = "项目";
            sheet.Cell(1, 2).Value = "规格";
            sheet.Cell(2, 1).Value = "P1";
            sheet.Cell(2, 2).Value = "S1";
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        return stream;
    }

    private sealed class CancelOnFirstReadStream(
        Stream inner,
        CancellationTokenSource cancellation) : Stream
    {
        private int _cancelled;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            CancelOnce();
            return inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            CancelOnce();
            return inner.Read(buffer);
        }

        public override int ReadByte()
        {
            CancelOnce();
            return inner.ReadByte();
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private void CancelOnce()
        {
            if (Interlocked.Exchange(ref _cancelled, 1) == 0)
            {
                cancellation.Cancel();
            }
        }
    }
}
