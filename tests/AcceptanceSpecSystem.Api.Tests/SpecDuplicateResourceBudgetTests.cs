using System.Net;
using System.Data.Common;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class SpecDuplicateResourceBudgetTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SpecDuplicateResourceBudgetTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("检测平台", "高速检测平台")]
    [InlineData("inspection platform", "high speed inspection platform")]
    [InlineData("高速视觉检测系统安全运行保障要求", "视觉检测系统安全运行保障要求高速")]
    [InlineData("A", "A平台")]
    [InlineData("尺寸-检测", "尺寸检测")]
    [InlineData(".", ",")]
    [InlineData("😀", "😁")]
    [InlineData("a-b", "ab")]
    public void 安全候选生成不应遗漏旧算法可识别的中英文短文本和标点样本(string leftProject, string rightProject)
    {
        using var governor = CreateGovernor();
        var result = SpecDuplicateDetectionService.Detect(
            [
                BuildSpec(1, leftProject, "重复规格文本"),
                BuildSpec(2, rightProject, "重复规格文本")
            ],
            governor,
            CancellationToken.None);

        (result.ExactGroupCount + result.SimilarGroupCount).Should().Be(1);
        result.ExactGroups.Concat(result.SimilarGroups)
            .Single().Items.Select(item => item.Id).Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public void 非空strict键相等但非精确规格仍应进入近重复判断()
    {
        using var governor = CreateGovernor();

        var result = SpecDuplicateDetectionService.Detect(
            [
                BuildSpec(1, "a-b", "abcdefghij"),
                BuildSpec(2, "ab", "abcdefghijX")
            ],
            governor,
            CancellationToken.None);

        result.SimilarGroupCount.Should().Be(1);
        result.SimilarGroups.Single().Items.Select(item => item.Id).Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public void 比较预算应在第一百万零一次完整相似度比较时拒绝且不返回部分结果()
    {
        using var governor = CreateGovernor(maxComparisons: 1_000_000);
        var specs = Enumerable.Range(1, 1_415)
            .Select(index => BuildSpec(index, "同桶", $"不同规格-{index:D4}"))
            .ToList();

        Action detect = () => SpecDuplicateDetectionService.Detect(
            specs,
            governor,
            CancellationToken.None);

        detect.Should().Throw<DuplicateAnalysisBudgetExceededException>()
            .Where(exception => exception.Code == 422 && exception.BudgetName == "duplicate_comparisons");
        governor.LastDuplicateComparison.Should().Be(1_000_001);
    }

    [Fact]
    public void 低比较预算的大桶不得在第一次比较前预先物化全部候选对()
    {
        using var governor = CreateGovernor(maxComparisons: 1);
        var specs = Enumerable.Range(1, 1_415)
            .Select(index => BuildSpec(index, "同桶", $"不同规格-{index:D4}"))
            .ToList();
        var before = GC.GetAllocatedBytesForCurrentThread();

        Action detect = () => SpecDuplicateDetectionService.Detect(
            specs,
            governor,
            CancellationToken.None);

        detect.Should().Throw<DuplicateAnalysisBudgetExceededException>();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        allocated.Should().BeLessThan(20L * 1024 * 1024,
            "比较预算为1时只能懒生成前两个唯一pair，不能先分配百万pair集合和排序列表");
        governor.LastDuplicateComparison.Should().Be(2);
    }

    [Fact]
    public void 跨桶样本只应计入安全候选集合中的实际完整比较()
    {
        using var governor = CreateGovernor();
        var specs = Enumerable.Range(1, 4)
            .Select(index => BuildSpec(index, "甲", $"甲规格-{index}"))
            .Concat(Enumerable.Range(5, 4).Select(index => BuildSpec(index, "乙", $"乙规格-{index}")))
            .ToList();

        _ = SpecDuplicateDetectionService.Detect(specs, governor, CancellationToken.None);

        governor.LastDuplicateComparison.Should().Be(12);
    }

    [Fact]
    public void 预取消令牌在只有精确组且没有近似候选时也应停止检测()
    {
        using var governor = CreateGovernor();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Action detect = () => SpecDuplicateDetectionService.Detect(
            [BuildSpec(1, "项目", "规格"), BuildSpec(2, "项目", "规格")],
            governor,
            cancellation.Token);

        detect.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void 检测中途取消不得返回部分结果()
    {
        using var cancellation = new CancellationTokenSource();
        using var governor = CreateGovernor(cancelAfterComparison: 3, cancellation: cancellation);
        var specs = Enumerable.Range(1, 10)
            .Select(index => BuildSpec(index, "同桶", $"不同规格-{index}"))
            .ToList();

        Action detect = () => SpecDuplicateDetectionService.Detect(specs, governor, cancellation.Token);

        detect.Should().Throw<OperationCanceledException>();
        governor.LastDuplicateComparison.Should().Be(3);
    }

    [Fact]
    public void 候选对生成到指定位置后取消应在继续生成时立即停止()
    {
        using var cancellation = new CancellationTokenSource();
        var specs = Enumerable.Range(1, 100)
            .Select(index => BuildSpec(index, "同桶", $"不同规格-{index}"))
            .ToList();
        using var enumerator = SpecDuplicateDetectionService
            .EnumerateCandidatePairs(specs, cancellation.Token)
            .GetEnumerator();
        for (var index = 0; index < 10; index++)
            enumerator.MoveNext().Should().BeTrue();

        cancellation.Cancel();
        Action moveNext = () => enumerator.MoveNext();

        moveNext.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void 一千四百一十四条多共同键项目应只访问每个外层候选对一次()
    {
        var commonProject = new string(Enumerable.Range(0, 500)
            .Select(index => (char)(0x4e00 + index))
            .ToArray());
        var specs = Enumerable.Range(1, 1_414)
            .Select(index => BuildSpec(index, commonProject, ((char)(0x6000 + index)).ToString()))
            .ToList();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using var governor = CreateGovernor(maxComparisons: 1_000_000);
        long pairCount = 0;
        foreach (var _ in SpecDuplicateDetectionService.EnumerateCandidatePairs(specs, timeout.Token))
        {
            pairCount++;
            governor.ValidateDuplicateComparisons(pairCount);
        }

        pairCount.Should().Be(998_991);
        governor.LastDuplicateComparison.Should().Be(998_991);
    }

    [Fact]
    public async Task 重复查询并发闸门应覆盖数据库查询并在等待取消和完成后释放()
    {
        var databaseName = $"duplicate-gate-{Guid.NewGuid():N}";
        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        var blocker = new BlockingDuplicateQueryInterceptor();
        await using var firstContext = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connectionString)
                .AddInterceptors(blocker)
                .Options);
        await firstContext.Database.EnsureCreatedAsync();
        await using var secondContext = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connectionString)
                .Options);
        using var governor = new ResourceBudgetGovernor(Microsoft.Extensions.Options.Options.Create(
            new ResourceBudgetOptions { MaxConcurrentHighCostMatching = 1 }));
        using var firstHarness = new QueryServiceHarness(firstContext, governor);
        using var secondHarness = new QueryServiceHarness(secondContext, governor);
        var access = new SpecAccessContext { UserId = 1, CompanyId = 1, IsAll = true };

        var first = firstHarness.Service.GetDuplicateGroupsAsync(access);
        await blocker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var waitingCancellation = new CancellationTokenSource();
        var second = secondHarness.Service.GetDuplicateGroupsAsync(
            access,
            cancellationToken: waitingCancellation.Token);
        second.IsCompleted.Should().BeFalse();
        waitingCancellation.Cancel();
        Func<Task> waitForSecond = async () => await second;
        await waitForSecond.Should().ThrowAsync<OperationCanceledException>();

        blocker.Release.TrySetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        var third = await secondHarness.Service.GetDuplicateGroupsAsync(access);
        third.ScannedCount.Should().Be(0);
    }

    [Fact]
    public async Task 重复查询422异常后应释放并发闸门供后续工作进入()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var customer = new Customer { Name = "异常释放客户", CreatedAt = DateTime.UtcNow };
        var file = new WordFile
        {
            CompanyId = 1,
            FileName = "exception-release.docx",
            FileHash = Guid.NewGuid().ToString("N"),
            FileContent = [],
            UploadedAt = DateTime.UtcNow
        };
        context.Customers.Add(customer);
        context.WordFiles.Add(file);
        await context.SaveChangesAsync();
        context.AcceptanceSpecs.AddRange(
            new AcceptanceSpec
            {
                CustomerId = customer.Id,
                Project = "项目一",
                Specification = "规格一",
                WordFileId = file.Id
            },
            new AcceptanceSpec
            {
                CustomerId = customer.Id,
                Project = "项目二",
                Specification = "规格二",
                WordFileId = file.Id
            });
        await context.SaveChangesAsync();
        using var governor = new ResourceBudgetGovernor(Microsoft.Extensions.Options.Options.Create(
            new ResourceBudgetOptions
            {
                MaxConcurrentHighCostMatching = 1,
                MaxDuplicateCandidates = 1
            }));
        using var harness = new QueryServiceHarness(
            context,
            governor,
            new ResourceBudgetOptions { MaxDuplicateCandidates = 1 });
        var access = new SpecAccessContext { UserId = 1, CompanyId = 1, IsAll = true };

        Func<Task> query = async () => await harness.Service.GetDuplicateGroupsAsync(access);
        await query.Should().ThrowAsync<DuplicateAnalysisBudgetExceededException>();

        using var recovered = await governor.AcquireAsync(ResourceWorkload.HighCostMatching)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task 第两千零一个有效候选应通过真实端点返回HTTP和业务码422()
    {
        var customerId = await SeedCandidatesAsync(2_001, uniqueSingleCharacterProjects: true);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/specs/duplicate-groups?customerId={customerId}");
        request.Headers.Add("X-Test-Role", "admin");
        request.Headers.Add("X-Test-Permissions", "*:*:*");

        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var payload = await response.ReadAsAsync<ApiResponse<object>>();
        payload.Code.Should().Be(422);
        payload.Message.Should().Be("重复分析数据量超出安全上限，请缩小筛选范围后重试");
    }

    [Fact]
    public async Task 两千个有效候选不应被maxGroups截断扫描或误报超限()
    {
        var customerId = await SeedCandidatesAsync(2_000, uniqueSingleCharacterProjects: true);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/specs/duplicate-groups?customerId={customerId}&maxGroups=1");
        request.Headers.Add("X-Test-Role", "admin");
        request.Headers.Add("X-Test-Permissions", "*:*:*");

        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.ReadAsAsync<ApiResponse<System.Text.Json.JsonElement>>();
        payload.Data.GetProperty("scannedCount").GetInt32().Should().Be(2_000);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("common")]
    public async Task 普通范围和全部范围都不得把其他公司的相同文本纳入重复组(string role)
    {
        var customerId = await SeedCrossCompanyDuplicatesAsync();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/specs/duplicate-groups?customerId={customerId}");
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-Permissions", "*:*:*");

        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.ReadAsAsync<ApiResponse<System.Text.Json.JsonElement>>();
        var data = payload.Data;
        data.GetProperty("scannedCount").GetInt32().Should().Be(2);
        data.GetProperty("exactGroups")[0].GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task 无有效数据范围应返回空结果而不是被同公司大数据量触发422()
    {
        var customerId = await SeedCandidatesAsync(2_001, uniqueSingleCharacterProjects: true);
        var userId = await SeedUserWithoutRoleAsync();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/specs/duplicate-groups?customerId={customerId}");
        request.Headers.Add("X-Test-Role", "none");
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        request.Headers.Add("X-Test-Permissions", "*:*:*");

        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.ReadAsAsync<ApiResponse<System.Text.Json.JsonElement>>();
        payload.Data.GetProperty("scannedCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task 候选上限加一应真实下推到SQLite查询而不是物化后截断()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new ReaderCommandCaptureInterceptor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var customer = new Customer { Name = "SQL上限客户", CreatedAt = DateTime.UtcNow };
        var file = new WordFile
        {
            CompanyId = 9,
            FileName = "sql-limit.docx",
            FileHash = Guid.NewGuid().ToString("N"),
            FileContent = [],
            UploadedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        db.WordFiles.Add(file);
        await db.SaveChangesAsync();
        db.AcceptanceSpecs.AddRange(
            new AcceptanceSpec
            {
                CustomerId = customer.Id,
                Project = "\t\r\n",
                Specification = "不得占用候选配额",
                WordFileId = file.Id,
                ImportedAt = DateTime.UtcNow
            },
            new AcceptanceSpec
            {
                CustomerId = customer.Id,
                Project = "\u00a0\u3000",
                Specification = "也不得占用候选配额",
                WordFileId = file.Id,
                ImportedAt = DateTime.UtcNow
            },
            new AcceptanceSpec
            {
                CustomerId = customer.Id,
                Project = "SQL项目-1",
                Specification = "SQL规格-1",
                WordFileId = file.Id,
                ImportedAt = DateTime.UtcNow
            },
            new AcceptanceSpec
            {
                CustomerId = customer.Id,
                Project = "SQL项目-2",
                Specification = "SQL规格-2",
                WordFileId = file.Id,
                ImportedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
        interceptor.Reset();

        var repository = new AcceptanceSpecSystem.Data.Repositories.AcceptanceSpecRepository(db);
        var result = await repository.GetDuplicateCandidatesAsync(
            new AcceptanceSpecSystem.Data.Repositories.AcceptanceSpecQueryOptions
            {
                CompanyId = 9,
                IsAll = true
            },
            2);

        result.Should().HaveCount(2);
        result.Select(item => item.Project).Should().Equal("SQL项目-1", "SQL项目-2");
        interceptor.LastReaderCommandText.Should().NotBeNull().And.Contain("LIMIT");
        interceptor.LastReaderParameterValues.Should().Contain(value => Convert.ToInt32(value) == 2);
    }

    private async Task<int> SeedCandidatesAsync(int count, bool uniqueSingleCharacterProjects)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var companyId = await db.SystemUsers.Where(user => user.Username == "admin")
            .Select(user => user.CompanyId)
            .SingleAsync();
        var customer = new Customer { Name = $"预算客户-{suffix}", CreatedAt = DateTime.UtcNow };
        var file = new WordFile
        {
            CompanyId = companyId,
            FileName = $"预算-{suffix}.docx",
            FileHash = suffix,
            FileContent = [],
            UploadedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        db.WordFiles.Add(file);
        await db.SaveChangesAsync();

        db.AcceptanceSpecs.AddRange(Enumerable.Range(0, count).Select(index => new AcceptanceSpec
        {
            CustomerId = customer.Id,
            Project = uniqueSingleCharacterProjects ? ((char)(0x4e00 + index)).ToString() : $"项目-{index}",
            Specification = $"规格-{index}",
            WordFileId = file.Id,
            ImportedAt = DateTime.UtcNow.AddSeconds(index)
        }));
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private async Task<int> SeedCrossCompanyDuplicatesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var companyId = await db.SystemUsers.Where(user => user.Username == "admin")
            .Select(user => user.CompanyId)
            .SingleAsync();
        var commonUserId = await db.SystemUsers.Where(user => user.Username == "common")
            .Select(user => user.Id)
            .SingleAsync();
        var commonRoleId = await db.AuthRoles.Where(role => role.Code == "common")
            .Select(role => role.Id)
            .SingleAsync();
        var existingScopes = await db.AuthRoleDataScopes
            .Where(dataScope => dataScope.RoleId == commonRoleId && dataScope.Resource == "spec")
            .ToListAsync();
        db.AuthRoleDataScopes.RemoveRange(existingScopes);
        db.AuthRoleDataScopes.Add(new AuthRoleDataScope
        {
            RoleId = commonRoleId,
            Resource = "spec",
            ScopeType = DataScopeType.Self,
            CreatedAt = DateTime.UtcNow
        });

        var customer = new Customer { Name = $"跨公司客户-{suffix}", CreatedAt = DateTime.UtcNow };
        var currentFile = new WordFile
        {
            CompanyId = companyId,
            FileName = $"当前公司-{suffix}.docx",
            FileHash = $"current-{suffix}",
            FileContent = [],
            UploadedAt = DateTime.UtcNow
        };
        var foreignFile = new WordFile
        {
            CompanyId = companyId + 1,
            FileName = $"其他公司-{suffix}.docx",
            FileHash = $"foreign-{suffix}",
            FileContent = [],
            UploadedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        db.WordFiles.AddRange(currentFile, foreignFile);
        await db.SaveChangesAsync();

        db.AcceptanceSpecs.AddRange(
            BuildCompanySpecs(customer.Id, currentFile.Id, commonUserId)
                .Concat(BuildCompanySpecs(customer.Id, foreignFile.Id, commonUserId)));
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private static IEnumerable<AcceptanceSpec> BuildCompanySpecs(
        int customerId,
        int fileId,
        int userId)
    {
        return
        [
            new AcceptanceSpec
            {
                CustomerId = customerId,
                Project = "跨公司重复项目",
                Specification = "跨公司重复规格",
                WordFileId = fileId,
                CreatedByUserId = userId,
                ImportedAt = DateTime.UtcNow
            },
            new AcceptanceSpec
            {
                CustomerId = customerId,
                Project = "跨公司重复项目",
                Specification = "跨公司重复规格",
                WordFileId = fileId,
                CreatedByUserId = userId,
                ImportedAt = DateTime.UtcNow.AddSeconds(1)
            }
        ];
    }

    private async Task<int> SeedUserWithoutRoleAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await db.SystemUsers.Select(user => user.CompanyId).FirstAsync();
        var user = new SystemUser
        {
            CompanyId = companyId,
            Username = $"无范围-{Guid.NewGuid():N}",
            PasswordHash = "unused",
            IsActive = true,
            PermissionVersion = 1,
            CreatedAt = DateTime.UtcNow
        };
        db.SystemUsers.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static AcceptanceSpecSystem.Data.Repositories.AcceptanceSpecDuplicateCandidate BuildSpec(
        int id,
        string project,
        string specification) => new()
    {
        Id = id,
        Project = project,
        Specification = specification,
        ImportedAt = DateTime.UtcNow
    };

    private static RecordingGovernor CreateGovernor(
        int maxComparisons = int.MaxValue,
        int? cancelAfterComparison = null,
        CancellationTokenSource? cancellation = null)
    {
        return new RecordingGovernor(maxComparisons, cancelAfterComparison, cancellation);
    }

    private sealed class RecordingGovernor(
        long maxComparisons,
        int? cancelAfterComparison,
        CancellationTokenSource? cancellation) : IResourceBudgetGovernor, IDisposable
    {
        public long LastDuplicateComparison { get; private set; }

        public ValueTask<ResourceBudgetLease> AcquireAsync(
            ResourceWorkload workload,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void ValidateDocumentSize(long bytes) { }
        public void ValidateWriteOperations(int operationCount) { }
        public void ValidateMatchingItems(int itemCount) { }
        public void ValidateDuplicateCandidates(int candidateCount)
        {
            if (candidateCount > 2_000)
                throw new DuplicateAnalysisBudgetExceededException("duplicate_candidates");
        }

        public void ValidateDuplicateComparisons(long comparisonCount)
        {
            LastDuplicateComparison = comparisonCount;
            if (cancelAfterComparison.HasValue && comparisonCount == cancelAfterComparison.Value)
                cancellation!.Cancel();
            if (comparisonCount > maxComparisons)
                throw new DuplicateAnalysisBudgetExceededException("duplicate_comparisons");
        }

        public void Dispose() { }
    }

    private sealed class ReaderCommandCaptureInterceptor : DbCommandInterceptor
    {
        public string? LastReaderCommandText { get; private set; }
        public IReadOnlyList<object?> LastReaderParameterValues { get; private set; } = [];

        public void Reset()
        {
            LastReaderCommandText = null;
            LastReaderParameterValues = [];
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            LastReaderCommandText = command.CommandText;
            LastReaderParameterValues = command.Parameters.Cast<DbParameter>()
                .Select(parameter => parameter.Value)
                .ToList();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class BlockingDuplicateQueryInterceptor : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"AcceptanceSpecs\"", StringComparison.Ordinal))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }

    private sealed class QueryServiceHarness : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly UnitOfWork _unitOfWork;

        public QueryServiceHarness(
            AppDbContext context,
            IResourceBudgetGovernor governor,
            ResourceBudgetOptions? options = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IAcceptanceSpecRepository>(new AcceptanceSpecRepository(context));
            _provider = services.BuildServiceProvider();
            _unitOfWork = new UnitOfWork(context, _provider);
            Service = new AcceptanceSpecQueryService(
                _unitOfWork,
                governor,
                Microsoft.Extensions.Options.Options.Create(options ?? new ResourceBudgetOptions()));
        }

        public AcceptanceSpecQueryService Service { get; }

        public void Dispose()
        {
            _unitOfWork.Dispose();
            _provider.Dispose();
        }
    }
}
