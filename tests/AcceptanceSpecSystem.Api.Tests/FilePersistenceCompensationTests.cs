using System.IO.Compression;
using System.Linq.Expressions;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class FilePersistenceCompensationTests
{
    [Theory]
    [InlineData(StorageFailureStage.Write)]
    [InlineData(StorageFailureStage.Move)]
    [InlineData(StorageFailureStage.Cancellation)]
    [InlineData(StorageFailureStage.Cleanup)]
    public async Task FileStorageSave_WhenWriteMoveOrCancellationFails_ShouldDeleteTemporaryFile(
        StorageFailureStage failureStage)
    {
        using var directory = new TemporaryDirectory();
        var service = new FaultInjectingFileStorageService(directory.Path, failureStage);

        Func<Task> act = () => service.SaveUploadedWordAsync("failure.docx", "content"u8.ToArray());

        if (failureStage == StorageFailureStage.Cancellation)
        {
            await act.Should().ThrowExactlyAsync<OperationCanceledException>();
        }
        else
        {
            var assertion = await act.Should().ThrowExactlyAsync<IOException>();
            assertion.Which.Message.Should().Be(
                failureStage == StorageFailureStage.Move
                    ? "injected move failure"
                    : "injected write failure");
        }

        service.LastTemporaryPath.Should().NotBeNullOrWhiteSpace();
        File.Exists(service.LastTemporaryPath).Should().Be(failureStage == StorageFailureStage.Cleanup);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UploadFileAsync_WhenRepositoryAddOrSaveFails_ShouldDeletePersistedFile(
        bool failOnAdd)
    {
        using var storage = new TrackingFileStorageService();
        var persistenceException = new InvalidOperationException(
            failOnAdd ? "repository add failed" : "database save failed");
        var repository = new FailingWordFileRepository(failOnAdd ? persistenceException : null);
        using var unitOfWork = new FailingUnitOfWork(
            repository,
            failOnAdd ? null : persistenceException);
        var fileAccessService = new DocumentFileAccessService(
            unitOfWork,
            storage,
            NoOpUploadedDocumentSnapshotInvalidator.Instance);
        var logger = new CollectingLogger<DocumentFileAppService>();
        var service = new DocumentFileAppService(unitOfWork, fileAccessService, logger);

        Func<Task> act = () => service.UploadFileAsync(CreateScope(), CreateValidWordUpload());

        var assertion = await act.Should().ThrowExactlyAsync<InvalidOperationException>();
        assertion.Which.Should().BeSameAs(persistenceException);
        storage.DeleteCalls.Should().Be(failOnAdd ? 1 : 0);
        storage.LastDeleteCancellationToken.CanBeCanceled.Should().BeFalse();
        storage.LastSavedRelativePath.Should().NotBeNullOrWhiteSpace();
        File.Exists(storage.GetAbsolutePath(storage.LastSavedRelativePath!)).Should().Be(!failOnAdd);
        if (!failOnAdd)
        {
            logger.Entries.Should().ContainSingle(entry =>
                entry.Level == LogLevel.Error &&
                ReferenceEquals(entry.Exception, persistenceException) &&
                entry.Message.Contains("保存结果不确定", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task UploadFileAsync_WhenCleanupFails_ShouldLogCleanupFailureAndPreservePersistenceException()
    {
        using var storage = new TrackingFileStorageService();
        var persistenceException = new InvalidOperationException("repository add failed");
        var cleanupException = new IOException("cleanup failed");
        storage.DeleteException = cleanupException;
        var repository = new FailingWordFileRepository(persistenceException);
        using var unitOfWork = new FailingUnitOfWork(repository, saveException: null);
        var fileAccessService = new DocumentFileAccessService(
            unitOfWork,
            storage,
            NoOpUploadedDocumentSnapshotInvalidator.Instance);
        var logger = new CollectingLogger<DocumentFileAppService>();
        var service = new DocumentFileAppService(unitOfWork, fileAccessService, logger);

        Func<Task> act = () => service.UploadFileAsync(CreateScope(), CreateValidWordUpload());

        var assertion = await act.Should().ThrowExactlyAsync<InvalidOperationException>();
        assertion.Which.Should().BeSameAs(persistenceException);
        storage.DeleteCalls.Should().Be(1);
        logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error &&
            ReferenceEquals(entry.Exception, cleanupException) &&
            entry.Message.Contains("清理已落盘文件失败", StringComparison.Ordinal));
        File.Exists(storage.GetAbsolutePath(storage.LastSavedRelativePath!)).Should().BeTrue();
    }

    private static SpecAccessContext CreateScope()
    {
        return new SpecAccessContext
        {
            UserId = 10,
            CompanyId = 20,
            OrgUnitId = 30,
            IsAll = true
        };
    }

    private static DocumentUploadCommand CreateValidWordUpload()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("[Content_Types].xml");
            archive.CreateEntry("word/document.xml");
        }

        return new DocumentUploadCommand(
            "sample.docx",
            UploadedFileType.WordDocx,
            stream.ToArray());
    }

    public enum StorageFailureStage
    {
        Write,
        Move,
        Cancellation,
        Cleanup
    }

    private sealed class FaultInjectingFileStorageService : FileStorageService
    {
        private readonly StorageFailureStage _failureStage;

        public FaultInjectingFileStorageService(string rootPath, StorageFailureStage failureStage)
            : base(
                new TestWebHostEnvironment(rootPath),
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["FileStorage:BasePath"] = rootPath
                    })
                    .Build())
        {
            _failureStage = failureStage;
        }

        public string? LastTemporaryPath { get; private set; }

        protected override async Task WriteAllBytesAsync(
            string path,
            byte[] content,
            CancellationToken cancellationToken)
        {
            LastTemporaryPath = path;
            await base.WriteAllBytesAsync(path, content, CancellationToken.None);

            if (_failureStage is StorageFailureStage.Write or StorageFailureStage.Cleanup)
            {
                throw new IOException("injected write failure");
            }

            if (_failureStage == StorageFailureStage.Cancellation)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        protected override void MoveFile(string sourcePath, string destinationPath, bool overwrite)
        {
            if (_failureStage == StorageFailureStage.Move)
            {
                throw new IOException("injected move failure");
            }

            base.MoveFile(sourcePath, destinationPath, overwrite);
        }

        protected override void DeleteFile(string path)
        {
            if (_failureStage == StorageFailureStage.Cleanup)
            {
                throw new IOException("injected cleanup failure");
            }

            base.DeleteFile(path);
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public TestWebHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            WebRootPath = contentRootPath;
        }

        public string ApplicationName { get; set; } = "FilePersistenceCompensationTests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TrackingFileStorageService : IFileStorageService, IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "AcceptanceSpecSystem.FilePersistenceTests",
            Guid.NewGuid().ToString("N"));

        public TrackingFileStorageService()
        {
            Directory.CreateDirectory(_root);
        }

        public string? LastSavedRelativePath { get; private set; }
        public int DeleteCalls { get; private set; }
        public CancellationToken LastDeleteCancellationToken { get; private set; }
        public Exception? DeleteException { get; set; }

        public Task<string> SaveUploadedWordAsync(
            string originalFileName,
            byte[] content,
            CancellationToken cancellationToken = default)
        {
            return SaveAsync("uploads/word-files", originalFileName, content, cancellationToken);
        }

        public Task<string> SaveUploadedExcelAsync(
            string originalFileName,
            byte[] content,
            CancellationToken cancellationToken = default)
        {
            return SaveAsync("uploads/excel-files", originalFileName, content, cancellationToken);
        }

        public Task<string> SaveFilledWordAsync(
            string originalFileName,
            byte[] content,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> SaveSmartFillPlaybackArchiveAsync(
            string originalFileName,
            byte[] content,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> SaveSmartFillResultArchiveAsync(
            string originalFileName,
            byte[] content,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Stream OpenReadStream(string relativePath) => throw new NotSupportedException();

        public Task<string> WriteHealthCheckFileAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public string GetAbsolutePath(string relativePath)
        {
            return System.IO.Path.Combine(_root, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        }

        public Task DeleteIfExistsAsync(
            string? relativePath,
            CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            LastDeleteCancellationToken = cancellationToken;
            if (DeleteException != null)
            {
                throw DeleteException;
            }

            if (!string.IsNullOrWhiteSpace(relativePath))
            {
                var fullPath = GetAbsolutePath(relativePath);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private async Task<string> SaveAsync(
            string directory,
            string originalFileName,
            byte[] content,
            CancellationToken cancellationToken)
        {
            var relativePath = $"{directory}/{Guid.NewGuid():N}{System.IO.Path.GetExtension(originalFileName)}";
            var fullPath = GetAbsolutePath(relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, content, cancellationToken);
            LastSavedRelativePath = relativePath;
            return relativePath;
        }
    }

    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        private readonly Exception? _saveException;

        public FailingUnitOfWork(IWordFileRepository wordFiles, Exception? saveException)
        {
            WordFiles = wordFiles;
            _saveException = saveException;
        }

        public ICustomerRepository Customers => null!;
        public IProcessRepository Processes => null!;
        public IMachineModelRepository MachineModels => null!;
        public IAcceptanceSpecRepository AcceptanceSpecs => null!;
        public IAcceptanceSpecReferenceEventRepository AcceptanceSpecReferenceEvents => null!;
        public IEmbeddingCacheRepository EmbeddingCaches => null!;
        public IWordFileRepository WordFiles { get; }
        public IAiServiceConfigRepository AiServiceConfigs => null!;
        public IPromptTemplateRepository PromptTemplates => null!;
        public IColumnMappingRuleRepository ColumnMappingRules => null!;
        public ISmartStructureRoutingRuleRepository SmartStructureRoutingRules => null!;
        public IDocumentTemplateRepository DocumentTemplates => null!;
        public ISystemUserRepository SystemUsers => null!;
        public IAuditLogRepository AuditLogs => null!;
        public IMatchingFillTaskRepository MatchingFillTasks => null!;
        public IExecutionHistoryRecordRepository ExecutionHistoryRecords => null!;
        public IOrgUnitRepository OrgUnits => null!;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return _saveException == null
                ? Task.FromResult(1)
                : Task.FromException<int>(_saveException);
        }

        public int SaveChanges() => throw new NotSupportedException();
        public Task BeginTransactionAsync() => throw new NotSupportedException();
        public Task CommitTransactionAsync() => throw new NotSupportedException();
        public Task RollbackTransactionAsync() => throw new NotSupportedException();
        public void Dispose() { }
    }

    private sealed class FailingWordFileRepository : IWordFileRepository
    {
        private readonly Exception? _addException;

        public FailingWordFileRepository(Exception? addException = null)
        {
            _addException = addException;
        }

        public Task<WordFile> AddAsync(WordFile entity, CancellationToken cancellationToken = default)
        {
            return _addException == null
                ? Task.FromResult(entity)
                : Task.FromException<WordFile>(_addException);
        }

        public IQueryable<WordFile> Query(bool asNoTracking = true) => Array.Empty<WordFile>().AsQueryable();
        public Task<WordFile?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult<WordFile?>(null);
        public Task<IReadOnlyList<WordFile>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WordFile>>([]);
        public Task<IReadOnlyList<WordFile>> FindAsync(Expression<Func<WordFile, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WordFile>>([]);
        public Task<WordFile?> FirstOrDefaultAsync(Expression<Func<WordFile, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult<WordFile?>(null);
        public Task AddRangeAsync(IEnumerable<WordFile> entities, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Update(WordFile entity) => throw new NotSupportedException();
        public void Remove(WordFile entity) => throw new NotSupportedException();
        public void RemoveRange(IEnumerable<WordFile> entities) => throw new NotSupportedException();
        public Task<bool> AnyAsync(Expression<Func<WordFile, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<int> CountAsync(Expression<Func<WordFile, bool>>? predicate = null, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<WordFile?> GetByHashAsync(string fileHash) => Task.FromResult<WordFile?>(null);
        public Task<bool> ExistsByHashAsync(string fileHash) => Task.FromResult(false);
        public Task<IReadOnlyList<WordFile>> GetAllWithoutContentAsync() => Task.FromResult<IReadOnlyList<WordFile>>([]);
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, exception, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, Exception? Exception, string Message);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "AcceptanceSpecSystem.FilePersistenceTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
