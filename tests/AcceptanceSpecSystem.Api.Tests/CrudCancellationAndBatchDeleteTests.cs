using System.Data.Common;
using System.Security.Claims;
using System.Reflection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Controllers;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application;
using AcceptanceSpecSystem.Application.Models;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit.Abstractions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class CrudCancellationAndBatchDeleteTests : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly SpecAccessContext FullScope = new()
    {
        UserId = 1,
        CompanyId = 1,
        IsAll = true,
        IncludeSelf = true
    };

    private readonly ApiWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public CrudCancellationAndBatchDeleteTests(
        ApiWebApplicationFactory factory,
        ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("process")]
    [InlineData("machine-model")]
    public async Task 主数据创建收到预取消令牌时不得写入数据库(string resource)
    {
        var name = $"cancel-create-{resource}-{Guid.NewGuid():N}";
        await using var scope = _factory.Services.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> act = resource switch
        {
            "customer" => () => scope.ServiceProvider.GetRequiredService<CustomerAppService>()
                .CreateAsync(name, cancellation.Token),
            "process" => () => scope.ServiceProvider.GetRequiredService<ProcessAppService>()
                .CreateAsync(name, cancellation.Token),
            _ => () => scope.ServiceProvider.GetRequiredService<MachineModelAppService>()
                .CreateAsync(name, cancellation.Token)
        };

        await act.Should().ThrowAsync<OperationCanceledException>();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var exists = resource switch
        {
            "customer" => await db.Customers.AnyAsync(item => item.Name == name),
            "process" => await db.Processes.AnyAsync(item => item.Name == name),
            _ => await db.MachineModels.AnyAsync(item => item.Name == name)
        };
        exists.Should().BeFalse();
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("process")]
    [InlineData("machine-model")]
    public async Task 主数据更新收到预取消令牌时不得改变实体(string resource)
    {
        var originalName = $"cancel-update-{resource}-{Guid.NewGuid():N}";
        var changedName = $"{originalName}-changed";
        await using var scope = _factory.Services.CreateAsyncScope();
        var id = await SeedReferenceAsync(scope.ServiceProvider, resource, originalName);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> act = resource switch
        {
            "customer" => () => scope.ServiceProvider.GetRequiredService<CustomerAppService>()
                .UpdateAsync(FullScope, id, changedName, cancellation.Token),
            "process" => () => scope.ServiceProvider.GetRequiredService<ProcessAppService>()
                .UpdateAsync(FullScope, id, changedName, cancellation.Token),
            _ => () => scope.ServiceProvider.GetRequiredService<MachineModelAppService>()
                .UpdateAsync(FullScope, id, changedName, cancellation.Token)
        };

        await act.Should().ThrowAsync<OperationCanceledException>();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ChangeTracker.Clear();
        var actualName = resource switch
        {
            "customer" => (await db.Customers.SingleAsync(item => item.Id == id)).Name,
            "process" => (await db.Processes.SingleAsync(item => item.Id == id)).Name,
            _ => (await db.MachineModels.SingleAsync(item => item.Id == id)).Name
        };
        actualName.Should().Be(originalName);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("process")]
    [InlineData("machine-model")]
    public async Task 主数据单删不存在编号且已取消时应在首个仓储调用停止(string resource)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> act = resource switch
        {
            "customer" => () => scope.ServiceProvider.GetRequiredService<CustomerAppService>()
                .DeleteAsync(int.MaxValue, cancellation.Token),
            "process" => () => scope.ServiceProvider.GetRequiredService<ProcessAppService>()
                .DeleteAsync(int.MaxValue, cancellation.Token),
            _ => () => scope.ServiceProvider.GetRequiredService<MachineModelAppService>()
                .DeleteAsync(int.MaxValue, cancellation.Token)
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("process")]
    public async Task 主数据子列表不存在编号且已取消时应在首个仓储调用停止(string resource)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> act = resource == "customer"
            ? () => scope.ServiceProvider.GetRequiredService<CustomerAppService>()
                .GetProcessesAsync(FullScope, int.MaxValue, cancellation.Token)
            : () => scope.ServiceProvider.GetRequiredService<ProcessAppService>()
                .GetSpecsAsync(FullScope, int.MaxValue, 1, 20, null, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task 四类控制器应将动作取消令牌传给数据范围解析()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var actions = new Func<Task>[]
        {
            () => WithUser(new CustomersController(null!, new CancellationCheckingScopeService()))
                .GetCustomers(cancellationToken: cancellation.Token),
            () => WithUser(new ProcessesController(null!, new CancellationCheckingScopeService()))
                .GetProcesses(cancellationToken: cancellation.Token),
            () => WithUser(new MachineModelsController(null!, new CancellationCheckingScopeService()))
                .GetMachineModels(cancellationToken: cancellation.Token),
            () => WithUser(new SpecsController(null!, new CancellationCheckingScopeService(), null!))
                .GetGroups(cancellation.Token)
        };

        foreach (var action in actions)
        {
            await action.Should().ThrowAsync<OperationCanceledException>();
        }
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("process")]
    [InlineData("machine-model")]
    [InlineData("spec")]
    public async Task 四类批删第501个唯一正编号应在数据库工作前拒绝(string resource)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var id = await SeedDeletableAsync(scope.ServiceProvider, resource);
        var ids = new[] { id }
            .Concat(Enumerable.Range(1_000_000, 500))
            .ToArray();

        var act = () => InvokeBatchDeleteAsync(scope.ServiceProvider, resource, ids);

        var exception = await act.Should().ThrowAsync<ApplicationServiceException>();
        exception.Which.Code.Should().Be(422);
        exception.Which.Message.Should().Be("单次最多删除500项，请缩小范围后重试");
        (await EntityExistsAsync(scope.ServiceProvider, resource, id)).Should().BeTrue();
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("process")]
    [InlineData("machine-model")]
    [InlineData("spec")]
    public async Task 四类批删允许500个唯一正编号(string resource)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var id = await SeedDeletableAsync(scope.ServiceProvider, resource);
        var ids = new[] { id }
            .Concat(Enumerable.Range(1_000_000, 499))
            .ToArray();

        var result = await InvokeBatchDeleteAsync(scope.ServiceProvider, resource, ids);

        if (result is BatchDeleteResultModel masterDataResult)
        {
            masterDataResult.SucceededIds.Should().Equal(id);
            masterDataResult.Failures.Should().HaveCount(499);
        }
        else
        {
            result.Should().Be(1);
        }

        (await EntityExistsAsync(scope.ServiceProvider, resource, id)).Should().BeFalse();
    }

    [Theory]
    [InlineData("customer", "请选择要删除的客户")]
    [InlineData("process", "请选择要删除的制程")]
    [InlineData("machine-model", "请选择要删除的机型")]
    [InlineData("spec", "请选择要删除的规格")]
    public async Task 四类批删过滤全部非正编号后应返回400(string resource, string message)
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        var act = () => InvokeBatchDeleteAsync(scope.ServiceProvider, resource, [0, -1, 0, -2]);

        var exception = await act.Should().ThrowAsync<ApplicationServiceException>();
        exception.Which.Code.Should().Be(400);
        exception.Which.Message.Should().Be(message);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("process")]
    [InlineData("machine-model")]
    [InlineData("spec")]
    public async Task 四类批删应忽略非正编号并按首次出现去重(string resource)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var id = await SeedDeletableAsync(scope.ServiceProvider, resource);

        var result = await InvokeBatchDeleteAsync(scope.ServiceProvider, resource, [0, id, id, -1, id]);

        if (result is BatchDeleteResultModel masterDataResult)
        {
            masterDataResult.SucceededIds.Should().Equal(id);
            masterDataResult.Failures.Should().BeEmpty();
        }
        else
        {
            result.Should().Be(1);
        }
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("process")]
    [InlineData("machine-model")]
    public async Task 三类主数据混合批删应保持输入顺序并只保存提交一次(string resource)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var fixture = await SeedMixedBatchAsync(scope.ServiceProvider, resource);
        var inner = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        using var unitOfWork = new CountingUnitOfWork(inner);
        var ids = new[] { fixture.MissingId, fixture.ReferencedId, fixture.EligibleSecondId, fixture.EligibleFirstId };

        var result = resource switch
        {
            "customer" => await new CustomerAppService(
                    unitOfWork,
                    null!,
                    NullLogger<CustomerAppService>.Instance)
                .BatchDeleteAsync(ids),
            "process" => await new ProcessAppService(
                    unitOfWork,
                    null!,
                    NullLogger<ProcessAppService>.Instance)
                .BatchDeleteAsync(ids),
            _ => await new MachineModelAppService(
                    unitOfWork,
                    null!,
                    NullLogger<MachineModelAppService>.Instance)
                .BatchDeleteAsync(ids)
        };

        result.Failures.Select(item => item.Id)
            .Should().Equal(fixture.MissingId, fixture.ReferencedId);
        result.SucceededIds.Should().Equal(fixture.EligibleSecondId, fixture.EligibleFirstId);
        unitOfWork.BeginCount.Should().Be(1);
        unitOfWork.SaveCount.Should().Be(1);
        unitOfWork.CommitCount.Should().Be(1);
        unitOfWork.RollbackCount.Should().Be(0);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("process")]
    [InlineData("machine-model")]
    public async Task 三类主数据批删未知数据库错误应回滚并原样抛出(string resource)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var id = await SeedDeletableAsync(scope.ServiceProvider, resource);
        var providerFailure = new DbUpdateException(
            "provider SQL detail",
            new InvalidOperationException("connection detail"));
        using var unitOfWork = new CountingUnitOfWork(
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            providerFailure);

        var act = () => InvokeMasterBatchDeleteAsync(unitOfWork, resource, [id]);

        var exception = await act.Should().ThrowAsync<DbUpdateException>();
        exception.Which.Should().BeSameAs(providerFailure);
        unitOfWork.RollbackCount.Should().Be(1);
        (await EntityExistsAsync(scope.ServiceProvider, resource, id)).Should().BeTrue();
    }

    [Theory]
    [InlineData("customer", "concurrency")]
    [InlineData("customer", "mysql-1451")]
    [InlineData("customer", "mysql-1217")]
    [InlineData("process", "concurrency")]
    [InlineData("process", "mysql-1451")]
    [InlineData("process", "mysql-1217")]
    [InlineData("machine-model", "concurrency")]
    [InlineData("machine-model", "mysql-1451")]
    [InlineData("machine-model", "mysql-1217")]
    public async Task 三类主数据批删已知数据库冲突应整批回滚并保留所有候选(
        string resource,
        string conflict)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var firstId = await SeedDeletableAsync(scope.ServiceProvider, resource);
        var secondId = await SeedDeletableAsync(scope.ServiceProvider, resource);
        Exception providerFailure = conflict switch
        {
            "concurrency" => new DbUpdateConcurrencyException("并发删除"),
            "mysql-1451" => new DbUpdateException(
                "删除失败",
                CreateMySqlException((MySqlErrorCode)1451, "provider detail")),
            _ => new DbUpdateException(
                "删除失败",
                CreateMySqlException((MySqlErrorCode)1217, "provider detail"))
        };
        using var unitOfWork = new CountingUnitOfWork(
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            providerFailure);

        var act = () => InvokeMasterBatchDeleteAsync(
            unitOfWork,
            resource,
            [firstId, secondId]);

        var exception = await act.Should().ThrowAsync<ApplicationServiceException>();
        exception.Which.Code.Should().Be(409);
        unitOfWork.BeginCount.Should().Be(1);
        unitOfWork.SaveCount.Should().Be(1);
        unitOfWork.RollbackCount.Should().Be(1);
        unitOfWork.CommitCount.Should().Be(0);
        (await EntityExistsAsync(scope.ServiceProvider, resource, firstId)).Should().BeTrue();
        (await EntityExistsAsync(scope.ServiceProvider, resource, secondId)).Should().BeTrue();
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("process")]
    [InlineData("machine-model")]
    public async Task 三类主数据批删取消不得被回滚失败覆盖(string resource)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var id = await SeedDeletableAsync(scope.ServiceProvider, resource);
        var cancellationFailure = new OperationCanceledException("原始取消");
        using var unitOfWork = new CountingUnitOfWork(
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            cancellationFailure,
            throwOnRollback: true);

        var act = () => InvokeMasterBatchDeleteAsync(unitOfWork, resource, [id]);

        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.Should().BeSameAs(cancellationFailure);
        unitOfWork.RollbackCount.Should().Be(1);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    public async Task 客户名称目标唯一冲突应映射409(string operation)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var id = await SeedReferenceAsync(
            scope.ServiceProvider,
            "customer",
            $"unique-original-{Guid.NewGuid():N}");
        var providerFailure = new DbUpdateException(
            "保存客户失败",
            CreateMySqlException(
                MySqlErrorCode.DuplicateKeyEntry,
                "Duplicate entry '目标客户' for key 'IX_Customers_Name'"));
        using var unitOfWork = new CountingUnitOfWork(
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            providerFailure);
        var service = new CustomerAppService(
            unitOfWork,
            null!,
            NullLogger<CustomerAppService>.Instance);

        Func<Task> act = operation == "create"
            ? () => service.CreateAsync($"unique-create-{Guid.NewGuid():N}")
            : () => service.UpdateAsync(
                FullScope,
                id,
                $"unique-update-{Guid.NewGuid():N}");

        var exception = await act.Should().ThrowAsync<ApplicationServiceException>();
        exception.Which.Code.Should().Be(409);
    }

    [Fact]
    public async Task 规格批删IsAll仍只能删除当前公司数据()
    {
        await using var serviceScope = _factory.Services.CreateAsyncScope();
        var fixture = await SeedTwoCompanySpecsAsync(serviceScope.ServiceProvider);
        var service = serviceScope.ServiceProvider.GetRequiredService<AcceptanceSpecAppService>();
        var companyScope = new SpecAccessContext
        {
            UserId = 1,
            CompanyId = fixture.CurrentCompanyId,
            IsAll = true,
            IncludeSelf = true
        };

        var deletedCount = await service.BatchDeleteAsync(
            companyScope,
            [fixture.CurrentCompanySpecId, fixture.OtherCompanySpecId]);

        deletedCount.Should().Be(1);
        (await EntityExistsAsync(
            serviceScope.ServiceProvider,
            "spec",
            fixture.CurrentCompanySpecId)).Should().BeFalse();
        (await EntityExistsAsync(
            serviceScope.ServiceProvider,
            "spec",
            fixture.OtherCompanySpecId)).Should().BeTrue();
    }

    [Theory]
    [InlineData(1451)]
    [InlineData(1217)]
    public async Task 规格批删ExecuteDelete直抛MySql外键冲突应映射409(int errorCode)
    {
        var providerFailure = CreateMySqlException(
            (MySqlErrorCode)errorCode,
            "provider detail");
        await using var fixture = await SpecExecuteDeleteFailureFixture.CreateAsync(providerFailure);

        var act = () => fixture.Service.BatchDeleteAsync(FullScope, [fixture.SpecId]);

        var exception = await act.Should().ThrowAsync<ApplicationServiceException>();
        exception.Which.Code.Should().Be(409);
        fixture.DeleteCommandCount.Should().Be(1);
        (await fixture.SpecExistsAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task 规格批删ExecuteDelete直抛未知MySql错误应原样上抛()
    {
        var providerFailure = CreateMySqlException(
            MySqlErrorCode.LockWaitTimeout,
            "provider detail");
        await using var fixture = await SpecExecuteDeleteFailureFixture.CreateAsync(providerFailure);

        var act = () => fixture.Service.BatchDeleteAsync(FullScope, [fixture.SpecId]);

        var exception = await act.Should().ThrowAsync<MySqlException>();
        exception.Which.Should().BeSameAs(providerFailure);
        fixture.DeleteCommandCount.Should().Be(1);
        (await fixture.SpecExistsAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task 规格批删ExecuteDelete取消不得包装为数据库冲突()
    {
        var cancellationFailure = new OperationCanceledException("provider cancellation");
        await using var fixture = await SpecExecuteDeleteFailureFixture.CreateAsync(cancellationFailure);

        var act = () => fixture.Service.BatchDeleteAsync(FullScope, [fixture.SpecId]);

        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.Should().BeSameAs(cancellationFailure);
        fixture.DeleteCommandCount.Should().Be(1);
        (await fixture.SpecExistsAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task 删除规格应级联向量缓存但保留来源Word文件()
    {
        await using var serviceScope = _factory.Services.CreateAsyncScope();
        var specId = await SeedDeletableAsync(serviceScope.ServiceProvider, "spec");
        var db = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var wordFileId = await db.AcceptanceSpecs
            .Where(spec => spec.Id == specId)
            .Select(spec => spec.WordFileId)
            .SingleAsync();
        db.EmbeddingCaches.Add(new EmbeddingCache
        {
            SpecId = specId,
            ModelName = "task12-model",
            Usage = "matching",
            TextHash = Guid.NewGuid().ToString("N"),
            Vector = [1, 2, 3]
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var deletedCount = await serviceScope.ServiceProvider
            .GetRequiredService<AcceptanceSpecAppService>()
            .BatchDeleteAsync(FullScope, [specId]);

        deletedCount.Should().Be(1);
        (await db.EmbeddingCaches.AnyAsync(cache => cache.SpecId == specId)).Should().BeFalse();
        (await db.WordFiles.IgnoreQueryFilters().AnyAsync(file => file.Id == wordFileId)).Should().BeTrue();
    }

    [Theory]
    [InlineData("customers", "customer")]
    [InlineData("processes", "process")]
    [InlineData("machine-models", "machine-model")]
    [InlineData("specs", "spec")]
    public async Task 四类批删API的501项请求应返回422和跟踪标识(
        string route,
        string resource)
    {
        using var client = _factory.CreateClient();
        var traceId = $"task12-422-{resource}-{Guid.NewGuid():N}";
        using var request = CreateBatchDeleteRequest(
            route,
            Enumerable.Range(1, 501).ToArray());
        request.Headers.Add("X-Client-Trace-Id", traceId);

        using var response = await client.SendAsync(request);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        body.Code.Should().Be(422);
        body.Message.Should().Be("单次最多删除500项，请缩小范围后重试");
        body.TraceId.Should().Be(traceId);

        if (resource == "spec")
            return;

        await using var serviceScope = _factory.Services.CreateAsyncScope();
        var audit = await serviceScope.ServiceProvider.GetRequiredService<AppDbContext>()
            .AuditLogs
            .AsNoTracking()
            .SingleAsync(log => log.ClientTraceId == traceId);
        audit.StatusCode.Should().Be(422);
        audit.Level.Should().Be(AuditLogLevel.Warning);
    }

    [Theory]
    [InlineData(typeof(CustomersController), "BatchDeleteCustomers")]
    [InlineData(typeof(ProcessesController), "BatchDeleteProcesses")]
    [InlineData(typeof(MachineModelsController), "BatchDeleteMachineModels")]
    public void 三类主数据批删应声明409和422响应(Type controllerType, string methodName)
    {
        var responseTypes = controllerType.GetMethod(methodName)!
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .ToArray();

        responseTypes.Should().Contain(409);
        responseTypes.Should().Contain(422);
    }

    [Fact]
    public async Task 未知数据库异常应由统一边界返回脱敏500()
    {
        using var factory = new UnknownDatabaseFailureFactory();
        using var client = factory.CreateClient();
        int customerId;
        await using (var serviceScope = factory.Services.CreateAsyncScope())
        {
            customerId = await SeedReferenceAsync(
                serviceScope.ServiceProvider,
                "customer",
                $"unknown-db-{Guid.NewGuid():N}");
        }

        var traceId = $"task12-500-{Guid.NewGuid():N}";
        using var request = CreateBatchDeleteRequest("customers", [customerId]);
        request.Headers.Add("X-Client-Trace-Id", traceId);

        using var response = await client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonSerializer.Deserialize<ApiResponse<JsonElement>>(
            raw,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        body.Code.Should().Be(500);
        body.TraceId.Should().Be(traceId);
        raw.Should().NotContain("provider SQL detail");
        raw.Should().NotContain("SELECT secret_table");
        raw.Should().NotContain(nameof(DbUpdateException));
    }

    [Fact]
    public async Task 删除客户应维持DocumentTemplate和Region数据库级联()
    {
        await using var serviceScope = _factory.Services.CreateAsyncScope();
        var customerId = await SeedReferenceAsync(
            serviceScope.ServiceProvider,
            "customer",
            $"cascade-customer-{Guid.NewGuid():N}");
        var db = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var template = new DocumentTemplate
        {
            CustomerId = customerId,
            TemplateName = "task12-cascade",
            HeadersFingerprint = "项目|规格",
            SpecificationColumnIndex = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Regions =
            [
                new DocumentTemplateRegion
                {
                    RegionIndex = 0,
                    SpecificationColumnIndex = 1
                }
            ]
        };
        db.DocumentTemplates.Add(template);
        await db.SaveChangesAsync();
        var regionId = template.Regions.Single().Id;
        db.ChangeTracker.Clear();

        var result = await serviceScope.ServiceProvider
            .GetRequiredService<CustomerAppService>()
            .BatchDeleteAsync([customerId]);

        result.SucceededIds.Should().Equal(customerId);
        (await db.DocumentTemplates.AnyAsync(item => item.Id == template.Id)).Should().BeFalse();
        (await db.DocumentTemplateRegions.AnyAsync(item => item.Id == regionId)).Should().BeFalse();
    }

    [MySqlSmokeFact]
    public async Task 真实MySQL应允许500项批删()
    {
        await using var database = await MySqlEmbeddingCacheTestDatabase.CreateAsync();
        _output.WriteLine($"MySQL测试库: {database.DatabaseName}");
        await database.MigrateAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => database.CreateDbContext());
        services.AddDataRepositories();
        await using var serviceProvider = services.BuildServiceProvider();
        await using var serviceScope = serviceProvider.CreateAsyncScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var customers = Enumerable.Range(1, 500)
            .Select(index => new Customer
            {
                Name = $"task12-mysql-500-{index:D3}-{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow
            })
            .ToArray();
        db.Customers.AddRange(customers);
        await db.SaveChangesAsync();
        var ids = customers.Select(customer => customer.Id).ToArray();

        var result = await new CustomerAppService(
                serviceScope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                null!,
                NullLogger<CustomerAppService>.Instance)
            .BatchDeleteAsync(ids);

        result.SucceededIds.Should().Equal(ids);
        result.Failures.Should().BeEmpty();
        (await db.Customers.CountAsync(customer => ids.Contains(customer.Id))).Should().Be(0);
    }

    private static TController WithUser<TController>(TController controller)
        where TController : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim("user_id", "1"),
                    new Claim("company_id", "1")
                ], "test"))
            }
        };
        return controller;
    }

    private static async Task<int> SeedReferenceAsync(
        IServiceProvider services,
        string resource,
        string name)
    {
        var db = services.GetRequiredService<AppDbContext>();
        switch (resource)
        {
            case "customer":
                var customer = new Customer { Name = name, CreatedAt = DateTime.UtcNow };
                db.Customers.Add(customer);
                await db.SaveChangesAsync();
                return customer.Id;
            case "process":
                var process = new Process { Name = name, CreatedAt = DateTime.UtcNow };
                db.Processes.Add(process);
                await db.SaveChangesAsync();
                return process.Id;
            default:
                var model = new MachineModel { Name = name, CreatedAt = DateTime.UtcNow };
                db.MachineModels.Add(model);
                await db.SaveChangesAsync();
                return model.Id;
        }
    }

    private static async Task<int> SeedDeletableAsync(IServiceProvider services, string resource)
    {
        if (resource != "spec")
        {
            return await SeedReferenceAsync(
                services,
                resource,
                $"batch-{resource}-{Guid.NewGuid():N}");
        }

        var customerId = await SeedReferenceAsync(
            services,
            "customer",
            $"batch-spec-customer-{Guid.NewGuid():N}");
        var created = await services.GetRequiredService<AcceptanceSpecAppService>().CreateAsync(
            FullScope,
            customerId,
            null,
            null,
            "batch-project",
            $"batch-spec-{Guid.NewGuid():N}",
            "OK",
            null);
        return created.Id;
    }

    private static async Task<object> InvokeBatchDeleteAsync(
        IServiceProvider services,
        string resource,
        IReadOnlyCollection<int> ids)
    {
        return resource switch
        {
            "customer" => await services.GetRequiredService<CustomerAppService>().BatchDeleteAsync(ids),
            "process" => await services.GetRequiredService<ProcessAppService>().BatchDeleteAsync(ids),
            "machine-model" => await services.GetRequiredService<MachineModelAppService>().BatchDeleteAsync(ids),
            _ => await services.GetRequiredService<AcceptanceSpecAppService>().BatchDeleteAsync(FullScope, ids)
        };
    }

    private static async Task<BatchDeleteResultModel> InvokeMasterBatchDeleteAsync(
        IUnitOfWork unitOfWork,
        string resource,
        IReadOnlyCollection<int> ids)
    {
        return resource switch
        {
            "customer" => await new CustomerAppService(
                    unitOfWork,
                    null!,
                    NullLogger<CustomerAppService>.Instance)
                .BatchDeleteAsync(ids),
            "process" => await new ProcessAppService(
                    unitOfWork,
                    null!,
                    NullLogger<ProcessAppService>.Instance)
                .BatchDeleteAsync(ids),
            _ => await new MachineModelAppService(
                    unitOfWork,
                    null!,
                    NullLogger<MachineModelAppService>.Instance)
                .BatchDeleteAsync(ids)
        };
    }

    private static async Task<bool> EntityExistsAsync(
        IServiceProvider services,
        string resource,
        int id)
    {
        var db = services.GetRequiredService<AppDbContext>();
        db.ChangeTracker.Clear();
        return resource switch
        {
            "customer" => await db.Customers.AnyAsync(item => item.Id == id),
            "process" => await db.Processes.AnyAsync(item => item.Id == id),
            "machine-model" => await db.MachineModels.AnyAsync(item => item.Id == id),
            _ => await db.AcceptanceSpecs.IgnoreQueryFilters().AnyAsync(item => item.Id == id)
        };
    }

    private static async Task<MixedBatchFixture> SeedMixedBatchAsync(
        IServiceProvider services,
        string resource)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var customer = new Customer
        {
            Name = $"mixed-base-customer-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);

        Customer? referencedCustomer = null;
        Customer? eligibleFirstCustomer = null;
        Customer? eligibleSecondCustomer = null;
        Process? referencedProcess = null;
        Process? eligibleFirstProcess = null;
        Process? eligibleSecondProcess = null;
        MachineModel? referencedModel = null;
        MachineModel? eligibleFirstModel = null;
        MachineModel? eligibleSecondModel = null;

        if (resource == "customer")
        {
            referencedCustomer = customer;
            eligibleFirstCustomer = new Customer
            {
                Name = $"mixed-eligible-customer-a-{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow
            };
            eligibleSecondCustomer = new Customer
            {
                Name = $"mixed-eligible-customer-b-{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow
            };
            db.Customers.AddRange(eligibleFirstCustomer, eligibleSecondCustomer);
        }
        else if (resource == "process")
        {
            referencedProcess = new Process { Name = $"mixed-process-ref-{Guid.NewGuid():N}" };
            eligibleFirstProcess = new Process { Name = $"mixed-process-a-{Guid.NewGuid():N}" };
            eligibleSecondProcess = new Process { Name = $"mixed-process-b-{Guid.NewGuid():N}" };
            db.Processes.AddRange(referencedProcess, eligibleFirstProcess, eligibleSecondProcess);
        }
        else
        {
            referencedModel = new MachineModel { Name = $"mixed-model-ref-{Guid.NewGuid():N}" };
            eligibleFirstModel = new MachineModel { Name = $"mixed-model-a-{Guid.NewGuid():N}" };
            eligibleSecondModel = new MachineModel { Name = $"mixed-model-b-{Guid.NewGuid():N}" };
            db.MachineModels.AddRange(referencedModel, eligibleFirstModel, eligibleSecondModel);
        }

        var wordFile = new WordFile
        {
            CompanyId = 1,
            FileName = $"mixed-{Guid.NewGuid():N}.docx",
            FileHash = Guid.NewGuid().ToString("N"),
            UploadedAt = DateTime.UtcNow
        };
        db.WordFiles.Add(wordFile);
        await db.SaveChangesAsync();

        db.AcceptanceSpecs.Add(new AcceptanceSpec
        {
            CustomerId = referencedCustomer?.Id ?? customer.Id,
            ProcessId = referencedProcess?.Id,
            MachineModelId = referencedModel?.Id,
            Project = "mixed-project",
            Specification = "mixed-spec",
            Acceptance = "OK",
            WordFileId = wordFile.Id,
            ImportedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return resource switch
        {
            "customer" => new MixedBatchFixture(
                int.MaxValue - 10,
                referencedCustomer!.Id,
                eligibleFirstCustomer!.Id,
                eligibleSecondCustomer!.Id),
            "process" => new MixedBatchFixture(
                int.MaxValue - 11,
                referencedProcess!.Id,
                eligibleFirstProcess!.Id,
                eligibleSecondProcess!.Id),
            _ => new MixedBatchFixture(
                int.MaxValue - 12,
                referencedModel!.Id,
                eligibleFirstModel!.Id,
                eligibleSecondModel!.Id)
        };
    }

    private static async Task<TwoCompanySpecFixture> SeedTwoCompanySpecsAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var currentCompanyId = await db.OrgCompanies
            .OrderBy(company => company.Id)
            .Select(company => company.Id)
            .FirstAsync();
        var otherCompany = new OrgCompany
        {
            Code = $"OTHER-{Guid.NewGuid():N}",
            Name = $"其他公司-{Guid.NewGuid():N}",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var customer = new Customer
        {
            Name = $"two-company-customer-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow
        };
        db.AddRange(otherCompany, customer);
        await db.SaveChangesAsync();

        var currentFile = new WordFile
        {
            CompanyId = currentCompanyId,
            FileName = $"current-{Guid.NewGuid():N}.docx",
            FileHash = Guid.NewGuid().ToString("N")
        };
        var otherFile = new WordFile
        {
            CompanyId = otherCompany.Id,
            FileName = $"other-{Guid.NewGuid():N}.docx",
            FileHash = Guid.NewGuid().ToString("N")
        };
        db.WordFiles.AddRange(currentFile, otherFile);
        await db.SaveChangesAsync();

        var currentSpec = new AcceptanceSpec
        {
            CustomerId = customer.Id,
            Project = "current-company",
            Specification = "current-company-spec",
            Acceptance = "OK",
            WordFileId = currentFile.Id
        };
        var otherSpec = new AcceptanceSpec
        {
            CustomerId = customer.Id,
            Project = "other-company",
            Specification = "other-company-spec",
            Acceptance = "OK",
            WordFileId = otherFile.Id
        };
        db.AcceptanceSpecs.AddRange(currentSpec, otherSpec);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return new TwoCompanySpecFixture(
            currentCompanyId,
            currentSpec.Id,
            otherSpec.Id);
    }

    private static HttpRequestMessage CreateBatchDeleteRequest(
        string route,
        IReadOnlyCollection<int> ids)
    {
        return route == "specs"
            ? new HttpRequestMessage(HttpMethod.Delete, "/api/specs/batch")
            {
                Content = JsonContent.Create(ids)
            }
            : new HttpRequestMessage(HttpMethod.Post, $"/api/{route}/batch-delete")
            {
                Content = JsonContent.Create(new { ids })
            };
    }

    private sealed record MixedBatchFixture(
        int MissingId,
        int ReferencedId,
        int EligibleFirstId,
        int EligibleSecondId);

    private sealed record TwoCompanySpecFixture(
        int CurrentCompanyId,
        int CurrentCompanySpecId,
        int OtherCompanySpecId);

    private sealed class CancellationCheckingScopeService : IAuthDataScopeService
    {
        public Task<DataScopeResult?> GetScopeAsync(
            int userId,
            int companyId,
            string resource,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<DataScopeResult?>(new DataScopeResult
            {
                UserId = userId,
                CompanyId = companyId,
                IsAll = true
            });
        }
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        private readonly IUnitOfWork _inner;
        private readonly Exception? _saveException;
        private readonly bool _throwOnRollback;
        private readonly Func<bool>? _shouldThrowOnSave;

        public CountingUnitOfWork(
            IUnitOfWork inner,
            Exception? saveException = null,
            bool throwOnRollback = false,
            Func<bool>? shouldThrowOnSave = null)
        {
            _inner = inner;
            _saveException = saveException;
            _throwOnRollback = throwOnRollback;
            _shouldThrowOnSave = shouldThrowOnSave;
        }

        public int BeginCount { get; private set; }
        public int SaveCount { get; private set; }
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }

        public ICustomerRepository Customers => _inner.Customers;
        public IProcessRepository Processes => _inner.Processes;
        public IMachineModelRepository MachineModels => _inner.MachineModels;
        public IAcceptanceSpecRepository AcceptanceSpecs => _inner.AcceptanceSpecs;
        public IEmbeddingCacheRepository EmbeddingCaches => _inner.EmbeddingCaches;
        public IWordFileRepository WordFiles => _inner.WordFiles;
        public IAiServiceConfigRepository AiServiceConfigs => _inner.AiServiceConfigs;
        public IPromptTemplateRepository PromptTemplates => _inner.PromptTemplates;
        public IColumnMappingRuleRepository ColumnMappingRules => _inner.ColumnMappingRules;
        public ISmartStructureRoutingRuleRepository SmartStructureRoutingRules => _inner.SmartStructureRoutingRules;
        public IDocumentTemplateRepository DocumentTemplates => _inner.DocumentTemplates;
        public ISystemUserRepository SystemUsers => _inner.SystemUsers;
        public IAuditLogRepository AuditLogs => _inner.AuditLogs;
        public IMatchingFillTaskRepository MatchingFillTasks => _inner.MatchingFillTasks;
        public IExecutionHistoryRecordRepository ExecutionHistoryRecords => _inner.ExecutionHistoryRecords;
        public IOrgUnitRepository OrgUnits => _inner.OrgUnits;

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (_saveException != null && (_shouldThrowOnSave == null || _shouldThrowOnSave()))
                throw _saveException;

            return await _inner.SaveChangesAsync(cancellationToken);
        }

        public int SaveChanges()
        {
            SaveCount++;
            if (_saveException != null && (_shouldThrowOnSave == null || _shouldThrowOnSave()))
                throw _saveException;

            return _inner.SaveChanges();
        }

        public async Task BeginTransactionAsync()
        {
            BeginCount++;
            await _inner.BeginTransactionAsync();
        }

        public async Task BeginTransactionAsync(CancellationToken cancellationToken)
        {
            BeginCount++;
            await _inner.BeginTransactionAsync(cancellationToken);
        }

        public async Task CommitTransactionAsync()
        {
            CommitCount++;
            await _inner.CommitTransactionAsync();
        }

        public async Task CommitTransactionAsync(CancellationToken cancellationToken)
        {
            CommitCount++;
            await _inner.CommitTransactionAsync(cancellationToken);
        }

        public async Task RollbackTransactionAsync()
        {
            RollbackCount++;
            if (_throwOnRollback)
                throw new InvalidOperationException("模拟回滚失败");

            await _inner.RollbackTransactionAsync();
        }

        public async Task RollbackTransactionAsync(CancellationToken cancellationToken)
        {
            RollbackCount++;
            if (_throwOnRollback)
                throw new InvalidOperationException("模拟回滚失败");

            await _inner.RollbackTransactionAsync(cancellationToken);
        }

        public void Dispose()
        {
            // 实际 scope 管理内部工作单元生命周期。
        }
    }

    private sealed class UnknownDatabaseFailureFactory : ApiWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUnitOfWork>();
                services.AddScoped<UnitOfWork>();
                services.AddScoped<IUnitOfWork>(serviceProvider =>
                {
                    var db = serviceProvider.GetRequiredService<AppDbContext>();
                    return new CountingUnitOfWork(
                        serviceProvider.GetRequiredService<UnitOfWork>(),
                        new DbUpdateException(
                            "provider SQL detail",
                            new InvalidOperationException("SELECT secret_table")),
                        shouldThrowOnSave: () => db.ChangeTracker
                            .Entries<Customer>()
                            .Any(entry => entry.State == EntityState.Deleted));
                });
            });
        }
    }

    private sealed class SpecExecuteDeleteFailureFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _serviceProvider;
        private readonly IServiceScope _serviceScope;
        private readonly DeleteCommandFailureInterceptor _interceptor;
        private readonly AppDbContext _db;

        private SpecExecuteDeleteFailureFixture(
            SqliteConnection connection,
            ServiceProvider serviceProvider,
            IServiceScope serviceScope,
            DeleteCommandFailureInterceptor interceptor,
            AppDbContext db,
            AcceptanceSpecAppService service,
            int specId)
        {
            _connection = connection;
            _serviceProvider = serviceProvider;
            _serviceScope = serviceScope;
            _interceptor = interceptor;
            _db = db;
            Service = service;
            SpecId = specId;
        }

        public AcceptanceSpecAppService Service { get; }
        public int SpecId { get; }
        public int DeleteCommandCount => _interceptor.DeleteCommandCount;

        public static async Task<SpecExecuteDeleteFailureFixture> CreateAsync(Exception failure)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var interceptor = new DeleteCommandFailureInterceptor(failure);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(options => options
                .UseSqlite(connection)
                .AddInterceptors(interceptor));
            services.AddDataRepositories();
            var serviceProvider = services.BuildServiceProvider();
            var serviceScope = serviceProvider.CreateScope();
            var db = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();

            var customer = new Customer
            {
                Name = $"spec-delete-provider-customer-{Guid.NewGuid():N}"
            };
            var wordFile = new WordFile
            {
                CompanyId = FullScope.CompanyId,
                FileName = "spec-delete-provider.docx",
                FileHash = Guid.NewGuid().ToString("N")
            };
            var spec = new AcceptanceSpec
            {
                Customer = customer,
                WordFile = wordFile,
                Project = "provider failure",
                Specification = "provider failure"
            };
            db.AcceptanceSpecs.Add(spec);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var service = new AcceptanceSpecAppService(
                serviceScope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                null!,
                NullLogger<AcceptanceSpecAppService>.Instance);
            return new SpecExecuteDeleteFailureFixture(
                connection,
                serviceProvider,
                serviceScope,
                interceptor,
                db,
                service,
                spec.Id);
        }

        public Task<bool> SpecExistsAsync()
        {
            return _db.AcceptanceSpecs
                .AsNoTracking()
                .AnyAsync(spec => spec.Id == SpecId);
        }

        public async ValueTask DisposeAsync()
        {
            _serviceScope.Dispose();
            await _serviceProvider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class DeleteCommandFailureInterceptor : DbCommandInterceptor
    {
        private readonly Exception _failure;

        public DeleteCommandFailureInterceptor(Exception failure)
        {
            _failure = failure;
        }

        public int DeleteCommandCount { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DELETE FROM \"AcceptanceSpecs\"", StringComparison.Ordinal))
            {
                DeleteCommandCount++;
                throw _failure;
            }

            return base.NonQueryExecutingAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }
    }

    private static MySqlException CreateMySqlException(MySqlErrorCode errorCode, string message)
    {
        var constructor = typeof(MySqlException).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(MySqlErrorCode), typeof(string), typeof(string), typeof(Exception)],
            modifiers: null);

        constructor.Should().NotBeNull();
        return (MySqlException)constructor!.Invoke([errorCode, "23000", message, null]);
    }
}
