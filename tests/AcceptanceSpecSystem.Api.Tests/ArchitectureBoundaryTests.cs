using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class ArchitectureBoundaryTests
{
    [Fact]
    public void LlmMatchingAssistParsing_ShouldDisposeEveryParsedJsonDocument()
    {
        var content = ReadFile(
            "src/AcceptanceSpecSystem.Core/Matching/Services/LlmMatchingAssistService.cs");
        var parseCount = Regex.Matches(content, @"TryParseJson\(raw, out var doc\)").Count;
        var disposeCount = Regex.Matches(content, @"using var parsedDocument = doc;").Count;

        parseCount.Should().Be(5);
        disposeCount.Should().Be(parseCount);
    }

    [Fact]
    public void Solution_ShouldContainApplicationProject_AndApiProject_ShouldReferenceIt()
    {
        var solutionContent = ReadFile("AcceptanceSpecSystem.sln");
        solutionContent.Should().Contain("AcceptanceSpecSystem.Application");

        var apiProjectContent = ReadFile("src/AcceptanceSpecSystem.Api/AcceptanceSpecSystem.Api.csproj");
        apiProjectContent.Should().Contain("AcceptanceSpecSystem.Application.csproj");
        apiProjectContent.Should().NotContain("AcceptanceSpecSystem.Core.csproj");
        apiProjectContent.Should().NotContain("AcceptanceSpecSystem.Data.csproj");
    }

    [Fact]
    public void Projects_ShouldNotCompileSourceFilesFromAnotherProject()
    {
        var sourceRoot = Path.Combine(GetRepositoryRoot(), "src");
        var projectFiles = Directory.GetFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories);

        foreach (var projectFile in projectFiles)
        {
            var document = XDocument.Load(projectFile);
            var linkedSources = document
                .Descendants("Compile")
                .Select(element => new
                {
                    Include = (string?)element.Attribute("Include"),
                    Link = (string?)element.Attribute("Link")
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Include))
                .Where(item => item.Link is not null || item.Include!.Contains("..", StringComparison.Ordinal))
                .ToList();

            linkedSources.Should().BeEmpty(
                $"{Path.GetRelativePath(sourceRoot, projectFile)} 不得通过 Compile Include/Link 编译其他项目源码");
        }
    }

    [Fact]
    public void Application_ShouldOwnSharedContractsAndProviderAdapters()
    {
        var contractFiles = new[]
        {
            "AuthRoleDtos.cs",
            "OrgUnitDtos.cs",
            "SystemUserDtos.cs",
            "DocumentDtos.cs",
            "ExcelImportDtos.cs",
            "AuditLogDtos.cs",
            "AiServiceDtos.cs",
            "ConfigurationDtos.cs",
            "AcceptanceSpecDtos.cs",
            "EmbeddingCacheWarmupDtos.cs"
        };

        foreach (var fileName in contractFiles)
        {
            var applicationPath = $"src/AcceptanceSpecSystem.Application/Contracts/{fileName}";
            File.Exists(Path.Combine(GetRepositoryRoot(), applicationPath.Replace('/', Path.DirectorySeparatorChar)))
                .Should().BeTrue($"{fileName} 应由 Application/Contracts 唯一拥有");
            ReadFile(applicationPath).Should().Contain("namespace AcceptanceSpecSystem.Application.Contracts;");
            File.Exists(Path.Combine(GetRepositoryRoot(), "src", "AcceptanceSpecSystem.Api", "DTOs", fileName))
                .Should().BeFalse($"{fileName} 不应继续物理归属 Api");
        }

        var providerPath = "src/AcceptanceSpecSystem.Application/Providers/CoreProviderAdapters.cs";
        File.Exists(Path.Combine(GetRepositoryRoot(), providerPath.Replace('/', Path.DirectorySeparatorChar)))
            .Should().BeTrue("Core provider adapter 应由 Application 唯一拥有");
        ReadFile(providerPath).Should().Contain("namespace AcceptanceSpecSystem.Application.Providers;");
        File.Exists(Path.Combine(GetRepositoryRoot(), "src", "AcceptanceSpecSystem.Data", "Providers", "CoreProviderAdapters.cs"))
            .Should().BeFalse("Data 目录不应继续保留由 Application 编译的 provider adapter 源码");
    }

    [Fact]
    public void ProtocolLayerPersistenceDependencies_ShouldNotExpandBeyondPhaseTwoBaseline()
    {
        var apiRoot = Path.Combine(GetRepositoryRoot(), "src", "AcceptanceSpecSystem.Api");
        var protocolDirectories = new[] { "Controllers", "Middleware", "Authorization" };

        foreach (var directory in protocolDirectories)
        {
            foreach (var sourceFile in Directory.GetFiles(Path.Combine(apiRoot, directory), "*.cs", SearchOption.AllDirectories))
            {
                var content = File.ReadAllText(sourceFile);
                var fileName = Path.GetFileName(sourceFile);

                content.Should().NotContain("AppDbContext",
                    $"{fileName} 属于协议层，不得直接依赖具体 DbContext");
                Regex.IsMatch(content, @"\bI[A-Za-z0-9]+Repository\b").Should().BeFalse(
                    $"{fileName} 属于协议层，不得直接依赖 Repository");

                content.Should().NotContain("IUnitOfWork",
                    $"{fileName} 属于协议层，不得直接依赖工作单元");
            }
        }
    }

    [Fact]
    public void Program_ShouldRegisterApplicationLayerExtension_InsteadOfInliningApplicationOwnedProviders()
    {
        var repositoryRoot = GetRepositoryRoot();
        var extensionPath = Path.Combine(
            repositoryRoot,
            "src/AcceptanceSpecSystem.Application/ServiceCollectionExtensions.cs".Replace('/', Path.DirectorySeparatorChar));
        File.Exists(extensionPath).Should().BeTrue("Application 层应暴露基础 DI 注册入口");

        var extensionContent = File.ReadAllText(extensionPath);
        extensionContent.Should().Contain("AddAcceptanceApplicationLayer", "Application 层应提供统一 DI 注册扩展");
        extensionContent.Should().Contain("IAiServiceConfigProvider", "Application 层应注册自身承载的 provider 适配器");
        extensionContent.Should().Contain("IPromptTemplateProvider", "Application 层应注册自身承载的 provider 适配器");

        var programContent = ReadFile("src/AcceptanceSpecSystem.Api/Program.cs");
        programContent.Should().Contain("AddAcceptanceApplicationLayer()", "API 启动应通过 Application 扩展注册应用层服务");
        programContent.Should().NotContain("AddScoped<IAiServiceConfigProvider, AiServiceConfigProvider>()",
            "Application 自有 provider 装配不应继续散落在 API Program 中");
        programContent.Should().NotContain("AddScoped<IPromptTemplateProvider, PromptTemplateProvider>()",
            "Application 自有 provider 装配不应继续散落在 API Program 中");
    }

    [Fact]
    public void Program_ShouldAuthenticateBeforeRateLimiter_WhenLimiterUsesUserClaims()
    {
        var programContent = ReadFile("src/AcceptanceSpecSystem.Api/Program.cs");

        programContent.Should().Contain("CreateFixedWindowLimiter", "限流分区会读取 HttpContext.User");
        programContent.IndexOf("app.UseAuthentication();", StringComparison.Ordinal)
            .Should().BeLessThan(
                programContent.IndexOf("app.UseRateLimiter();", StringComparison.Ordinal),
                "限流按用户分区前必须先完成认证，否则会退化为按 IP 分区");
    }

    [Fact]
    public void Program_ShouldApplyTrustedForwardedHeadersBeforeIpConsumers()
    {
        var programContent = ReadFile("src/AcceptanceSpecSystem.Api/Program.cs");
        var forwardedHeadersIndex = programContent.IndexOf("app.UseForwardedHeaders", StringComparison.Ordinal);

        forwardedHeadersIndex.Should().BeGreaterThan(-1);
        forwardedHeadersIndex.Should().BeLessThan(
            programContent.IndexOf("app.UseMiddleware<RequestTracingMiddleware>();", StringComparison.Ordinal));
        forwardedHeadersIndex.Should().BeLessThan(
            programContent.IndexOf("app.UseRateLimiter();", StringComparison.Ordinal));
    }

    [Fact]
    public void ViteProxyFallback_ShouldMatchApiLaunchPort()
    {
        var viteConfigContent = ReadFile("web/vite.config.ts");

        viteConfigContent.Should().Contain("http://localhost:5291");
        viteConfigContent.Should().NotContain(
            "http://localhost:5843",
            "Vite 代理兜底端口应和 API launchSettings 保持一致，避免未配置环境变量时代理漂移");
    }

    [Fact]
    public void DataProject_ShouldNotReferenceCoreProject()
    {
        var dataProjectContent = ReadFile("src/AcceptanceSpecSystem.Data/AcceptanceSpecSystem.Data.csproj");
        dataProjectContent.Should().NotContain("AcceptanceSpecSystem.Core.csproj");
    }

    [Fact]
    public void RbacControllers_ShouldNotDirectlyDependOnAppDbContext()
    {
        var controllerFiles = new[]
        {
            "src/AcceptanceSpecSystem.Api/Controllers/AuthRolesController.cs",
            "src/AcceptanceSpecSystem.Api/Controllers/AuthPermissionsController.cs",
            "src/AcceptanceSpecSystem.Api/Controllers/OrgUnitsController.cs",
            "src/AcceptanceSpecSystem.Api/Controllers/SystemUsersController.cs"
        };

        foreach (var relativePath in controllerFiles)
        {
            var content = ReadFile(relativePath);
            content.Should().NotContain("AppDbContext", $"{relativePath} 应通过 Application 用例服务而不是直接依赖 DbContext");
        }
    }

    [Fact]
    public void BaseDataControllers_ShouldDelegateToApplicationUseCaseServices()
    {
        var controllerFiles = new (string RelativePath, string ServiceName)[]
        {
            ("src/AcceptanceSpecSystem.Api/Controllers/CustomersController.cs", "CustomerAppService"),
            ("src/AcceptanceSpecSystem.Api/Controllers/ProcessesController.cs", "ProcessAppService"),
            ("src/AcceptanceSpecSystem.Api/Controllers/MachineModelsController.cs", "MachineModelAppService"),
            ("src/AcceptanceSpecSystem.Api/Controllers/SpecsController.cs", "AcceptanceSpecAppService")
        };

        foreach (var (relativePath, serviceName) in controllerFiles)
        {
            var content = ReadFile(relativePath);
            content.Should().Contain(serviceName, $"{relativePath} 应委派给 Application 用例服务");
            content.Should().NotContain("IUnitOfWork", $"{relativePath} 不应继续直接编排工作单元");
        }
    }

    [Fact]
    public void ApplicationProject_ShouldOwnReferenceDataAndSpecUseCaseServices()
    {
        var repositoryRoot = GetRepositoryRoot();
        var serviceFiles = new[]
        {
            "src/AcceptanceSpecSystem.Application/Services/CustomerAppService.cs",
            "src/AcceptanceSpecSystem.Application/Services/ProcessAppService.cs",
            "src/AcceptanceSpecSystem.Application/Services/MachineModelAppService.cs",
            "src/AcceptanceSpecSystem.Application/Services/AcceptanceSpecAppService.cs",
            "src/AcceptanceSpecSystem.Application/Services/AcceptanceSpecQueryService.cs"
        };

        foreach (var relativePath in serviceFiles)
        {
            File.Exists(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .Should().BeTrue($"{relativePath} 应作为 Application 层用例或查询服务存在");
        }

        var serviceCollectionContent = ReadFile("src/AcceptanceSpecSystem.Application/ServiceCollectionExtensions.cs");
        serviceCollectionContent.Should().Contain("CustomerAppService");
        serviceCollectionContent.Should().Contain("ProcessAppService");
        serviceCollectionContent.Should().Contain("MachineModelAppService");
        serviceCollectionContent.Should().Contain("AcceptanceSpecAppService");
        serviceCollectionContent.Should().Contain("AcceptanceSpecQueryService");

        var queryContent = ReadFile("src/AcceptanceSpecSystem.Application/Services/AcceptanceSpecQueryService.cs");
        queryContent.Should().Contain("GetPagedWithFilterAsync", "复杂规格分页查询应由专用 query service 承接");
        queryContent.Should().Contain("GetGroupSummaryWithFilterAsync", "复杂分组汇总查询应由专用 query service 承接");

        ReadFile("src/AcceptanceSpecSystem.Application/Services/CustomerAppService.cs")
            .Should().Contain("AcceptanceSpecQueryService", "客户用例服务应复用专用 query service 统计规格关系");
        ReadFile("src/AcceptanceSpecSystem.Application/Services/ProcessAppService.cs")
            .Should().Contain("AcceptanceSpecQueryService", "制程用例服务应复用专用 query service 处理规格查询");
        ReadFile("src/AcceptanceSpecSystem.Application/Services/MachineModelAppService.cs")
            .Should().Contain("AcceptanceSpecQueryService", "机型用例服务应复用专用 query service 统计规格关系");

        var specAppContent = ReadFile("src/AcceptanceSpecSystem.Application/Services/AcceptanceSpecAppService.cs");
        specAppContent.Should().Contain("AcceptanceSpecQueryService", "规格用例服务应通过专用 query service 承接复杂只读查询");
        specAppContent.Should().Contain("IUnitOfWork", "规格用例服务仍应通过 UoW 承接写入路径");
        specAppContent.Should().NotContain("GetPagedWithFilterAsync(", "复杂只读查询职责应停留在 query service，而不是再次混回写入用例服务");
        specAppContent.Should().NotContain("GetGroupSummaryWithFilterAsync(", "复杂只读查询职责应停留在 query service，而不是再次混回写入用例服务");
    }

    [Fact]
    public void MatchingControllers_ShouldDependOnFocusedApplicationServices()
    {
        var controllerFiles = new[]
        {
            "src/AcceptanceSpecSystem.Api/Controllers/MatchingApiControllerBase.cs",
            "src/AcceptanceSpecSystem.Api/Controllers/MatchingPreviewController.cs",
            "src/AcceptanceSpecSystem.Api/Controllers/MatchingExecutionController.cs",
            "src/AcceptanceSpecSystem.Api/Controllers/MatchingTaskController.cs"
        };

        foreach (var relativePath in controllerFiles)
        {
            var content = ReadFile(relativePath);
            content.Should().NotContain("MatchingWorkflowService", $"{relativePath} 不应继续依赖巨型工作流服务");
        }
    }

    [Fact]
    public void MatchingPreviewAppService_ShouldNotDependOnMatchingWorkflowService()
    {
        var content = ReadFile("src/AcceptanceSpecSystem.Application/Services/MatchingPreviewAppService.cs");

        content.Should().NotContain("MatchingWorkflowService", "预览应用服务应承载独立预览用例，而不是继续透传巨型工作流服务");
    }

    [Fact]
    public void MatchingWorkflowService_ShouldNotKeepPreviewEntrypoints()
    {
        var content = ReadFile("src/AcceptanceSpecSystem.Application/Services/MatchingWorkflowSupportService.cs");

        content.Should().NotContain("public async Task<MatchingOperationResult<MatchPreviewResponse>> PreviewAsync(",
            "预览入口已经拆到独立应用服务，不应继续留在巨型工作流服务中");
        content.Should().NotContain("public async Task<MatchingOperationResult<BatchPreviewResponse>> BatchPreviewAsync(",
            "批量预览入口已经拆到独立应用服务，不应继续留在巨型工作流服务中");
        content.Should().NotContain("public async Task<MatchingOperationResult<SimilarityResponse>> ComputeSimilarityAsync(",
            "相似度入口已经拆到独立应用服务，不应继续留在巨型工作流服务中");
    }

    [Fact]
    public void MatchingTaskAppService_ShouldNotDependOnMatchingWorkflowService()
    {
        var content = ReadFile("src/AcceptanceSpecSystem.Application/Services/MatchingTaskAppService.cs");

        content.Should().NotContain("MatchingWorkflowService", "任务下载应用服务应承载独立下载用例，而不是继续透传巨型工作流服务");
    }

    [Fact]
    public void MatchingExecutionFacade_ShouldBeRemoved_AfterControllerUsesSplitUseCases()
    {
        File.Exists(Path.Combine(GetRepositoryRoot(),
                "src/AcceptanceSpecSystem.Application/Services/MatchingExecutionAppService.cs".Replace('/', Path.DirectorySeparatorChar)))
            .Should().BeFalse("迁移期聚合 façade 应在 2.6 删除");
        var controller = ReadFile("src/AcceptanceSpecSystem.Api/Controllers/MatchingExecutionController.cs");
        controller.Should().Contain("IMatchingLlmStreamAppService");
        controller.Should().Contain("IMatchingFillExecutionAppService");
        controller.Should().NotContain("IMatchingExecutionAppService");
    }

    [Fact]
    public void MatchingWorkflowService_ShouldNotKeepTaskDownloadEntrypoint()
    {
        var content = ReadFile("src/AcceptanceSpecSystem.Application/Services/MatchingWorkflowSupportService.cs");

        content.Should().NotContain("public async Task<MatchingDownloadResult> DownloadAsync(",
            "任务下载入口已经拆到独立应用服务，不应继续留在巨型工作流服务中");
    }

    [Fact]
    public void StrictReuseLegacyService_ShouldBeRemoved()
    {
        var repositoryRoot = GetRepositoryRoot();
        File.Exists(Path.Combine(
                repositoryRoot,
                "src/AcceptanceSpecSystem.Api/Services/StrictReuseAppService.cs".Replace('/', Path.DirectorySeparatorChar)))
            .Should()
            .BeFalse("strict reuse 已从当前主链移除，不应继续保留后端服务实现");
    }

    [Fact]
    public void MatchingWorkflowService_ShouldNotKeepStrictReuseEntrypoints()
    {
        var content = ReadFile("src/AcceptanceSpecSystem.Application/Services/MatchingWorkflowSupportService.cs");

        content.Should().NotContain("public async Task<MatchingOperationResult<StrictReusePreviewResponse>> PreviewStrictReuseAsync(",
            "严格复用预检入口已经拆到独立应用服务，不应继续留在巨型工作流服务中");
        content.Should().NotContain("public async Task<MatchingOperationResult<StrictReuseExecuteResponse>> ExecuteStrictReuseAsync(",
            "严格复用执行入口已经拆到独立应用服务，不应继续留在巨型工作流服务中");
    }

    [Fact]
    public void MatchingWorkflowService_ShouldNotKeepExecutionOrSnapshotEntrypoints()
    {
        var content = ReadFile("src/AcceptanceSpecSystem.Application/Services/MatchingWorkflowSupportService.cs");

        content.Should().NotContain("public async Task LlmStreamAsync(",
            "LLM 流式复核入口应拆到独立应用服务，不应继续留在巨型工作流服务中");
        content.Should().NotContain("public async Task<MatchingOperationResult<ExecuteFillResponse>> ExecuteFillAsync(",
            "执行填充入口应拆到独立应用服务，不应继续留在巨型工作流服务中");
        content.Should().NotContain("public async Task<MatchingOperationResult<ExecuteFillResponse>> BatchExecuteFillAsync(",
            "批量执行填充入口应拆到独立应用服务，不应继续留在巨型工作流服务中");
        content.Should().NotContain("private async Task SaveFillTaskSnapshotAsync(",
            "任务快照保存应拆到独立快照服务，不应继续留在巨型工作流服务中");
        content.Should().NotContain("private async Task<FillTaskResult?> LoadFillTaskSnapshotAsync(",
            "任务快照读取应拆到独立快照服务，不应继续留在巨型工作流服务中");
    }

    [Fact]
    public void MatchingTaskSnapshotService_ShouldExist_AndBeSharedByExecutionAndDownloadServices()
    {
        var repositoryRoot = GetRepositoryRoot();
        var snapshotServicePath = Path.Combine(
            repositoryRoot,
            "src/AcceptanceSpecSystem.Application/Services/MatchingTaskSnapshotService.cs".Replace('/', Path.DirectorySeparatorChar));

        File.Exists(snapshotServicePath).Should().BeTrue("任务快照应收敛到独立共享服务，供执行与下载共同使用");

        var snapshotContent = File.ReadAllText(snapshotServicePath);
        snapshotContent.Should().Contain("SaveAsync(", "快照服务应提供统一保存入口");
        snapshotContent.Should().Contain("LoadAsync(", "快照服务应提供统一读取入口");
        snapshotContent.Should().Contain("EnsureTaskOwnership", "快照服务应统一校验任务归属");

        ReadFile("src/AcceptanceSpecSystem.Application/Services/MatchingWorkflowSupportService.cs")
            .Should().Contain("MatchingTaskSnapshotService",
                "执行填充核心协作组件应通过共享快照服务持久化任务结果");
        ReadFile("src/AcceptanceSpecSystem.Application/Services/MatchingTaskAppService.cs")
            .Should().Contain("MatchingTaskSnapshotService",
                "下载应用服务应通过共享快照服务读取任务结果");
    }

    [Fact]
    public void DocumentsController_ShouldNotCarryInlineImportWorkflowImplementation()
    {
        var content = ReadFile("src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs");

        content.Should().Contain("DocumentImportAppService", "导入接口应通过独立应用服务完成编排");
        content.Should().NotContain("ProcessImportRowAsync(", "导入逐行处理应迁移到 Application 用例服务");
        content.Should().NotContain("TryApplyPendingDecisionAsync(", "差异决策回放应迁移到 Application 用例服务");
        content.Should().NotContain("CreateDuplicateDetectionSessionAsync(", "导入 AI 去重会话应迁移到 Application 用例服务");
    }

    [Fact]
    public void SharedDocumentCollaborators_ShouldExist_AndBeReusedByDocumentAndMatchingServices()
    {
        var repositoryRoot = GetRepositoryRoot();
        var collaboratorFiles = new[]
        {
            "src/AcceptanceSpecSystem.Api/Services/DocumentFileAccessService.cs",
            "src/AcceptanceSpecSystem.Api/Services/DocumentTableAccessService.cs",
            "src/AcceptanceSpecSystem.Api/Services/MatchingResultWriteBackService.cs"
        };

        foreach (var relativePath in collaboratorFiles)
        {
            File.Exists(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .Should().BeTrue($"{relativePath} 应作为 2.3 的共享协作组件存在");
        }

        ReadFile("src/AcceptanceSpecSystem.Application/Services/DocumentImportAppService.cs")
            .Should().Contain("IDocumentFileAccessService",
                "导入应用服务应复用共享文件读取组件");
        ReadFile("src/AcceptanceSpecSystem.Application/Services/MatchingPreviewAppService.cs")
            .Should().Contain("IBatchReplyDocumentTablePort",
                "匹配预览应用服务应复用共享表格提取组件");
        ReadFile("src/AcceptanceSpecSystem.Application/Services/MatchingWorkflowSupportService.cs")
            .Should().Contain("IMatchingResultWriteBackPort",
                "执行填充协作组件应复用共享结果写回组件");
        ReadFile("src/AcceptanceSpecSystem.Application/Services/MatchingTaskAppService.cs")
            .Should().Contain("IMatchingResultWriteBackPort",
                "下载应用服务应复用共享结果写回组件");
        ReadFile("src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs")
            .Should().NotContain("DocumentTableAccessService",
                "文档控制器不应直接依赖 parser 基础设施适配器");
        ReadFile("src/AcceptanceSpecSystem.Application/Services/DocumentTableQueryAppService.cs")
            .Should().Contain("IDocumentImportTableReader",
                "表格列表与预览用例应通过 Application 端口访问 parser 适配器");
    }

    [Fact]
    public void DocumentsController_ShouldDelegateDocumentResourceUseCases_ToApplicationServices()
    {
        var content = ReadFile("src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs");

        content.Should().Contain("DocumentFileAppService",
            "文档资源接口应通过独立应用服务完成文件列表、上传、预览与删除编排");
        content.Should().Contain("IDocumentTableQueryAppService",
            "文档表格列表与预览应通过 Application 用例完成");
        content.Should().NotContain("private readonly DocumentTableAccessService",
            "控制器不得直接依赖 parser 基础设施实现");
        content.Should().NotContain("private readonly DocumentServiceFactory",
            "文档控制器不应再直接依赖文档工厂");
        content.Should().NotContain("private readonly IFileStorageService",
            "文档控制器不应再直接依赖文件存储");
        content.Should().NotContain("OpenWordFileReadStream(",
            "文档控制器不应再内联文件读取实现");
        content.Should().NotContain("BuildSpecCountByFileAsync(",
            "文档控制器不应再内联列表统计查询");
        content.Should().NotContain("ApplyWordFileScopeToQuery(",
            "文档控制器不应再内联文件范围过滤实现");
        content.Should().NotContain("GetAccessibleWordFileAsync(",
            "文档控制器不应再内联单文件归属校验实现");
        content.Should().NotContain("SaveUploadedExcelAsync(",
            "文档控制器不应再直接处理上传文件持久化");
        content.Should().NotContain("SaveUploadedWordAsync(",
            "文档控制器不应再直接处理上传文件持久化");
        content.Should().NotContain("ExtractTableDataAsync(",
            "文档控制器不应再直接调用表格提取实现");
        content.Should().NotContain("_documentTableAccessService.GetTablesAsync(",
            "文档控制器不应直接调用 parser 基础设施实现");
        content.Should().NotContain("DeleteIfExistsAsync(",
            "文档控制器不应再直接删除物理文件");
    }

    [Fact]
    public void DocumentTablePresentation_ShouldBeOwnedByApplicationUseCase()
    {
        var controller = ReadFile("src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs");
        var appService = ReadFile("src/AcceptanceSpecSystem.Application/Services/DocumentTableQueryAppService.cs");
        var parserAdapter = ReadFile("src/AcceptanceSpecSystem.Api/Services/DocumentTableAccessService.cs");

        controller.Should().Contain("IDocumentTableQueryAppService");
        controller.Should().NotContain("DocumentTableAccessService");
        appService.Should().Contain("TableInfoDto");
        appService.Should().Contain("TableDataDto");
        appService.Should().Contain("MapStructuredCellValue");
        appService.Should().Contain("FormatPreviewCellText");
        parserAdapter.Should().Contain("DocumentTableQueryAppService.MapTableInfos");
        parserAdapter.Should().Contain("DocumentTableQueryAppService.MapPreview");
        parserAdapter.Should().NotContain("new TableInfoDto");
        parserAdapter.Should().NotContain("new TableDataDto");
        parserAdapter.Should().NotContain("private static StructuredCellValueDto MapStructuredCellValue");
    }

    [Fact]
    public void SimpleApiAppServices_ShouldExposeInterfaces_AndControllersShouldDependOnInterfaces()
    {
        var serviceCollectionContent = string.Join(
            Environment.NewLine,
            ReadFile("src/AcceptanceSpecSystem.Api/ServiceCollectionExtensions.cs"),
            ReadFile("src/AcceptanceSpecSystem.Application/ServiceCollectionExtensions.cs"));

        var services = new[]
        {
            ("src/AcceptanceSpecSystem.Application/Services/DashboardAppService.cs",
                "IDashboardAppService",
                "DashboardAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/DashboardController.cs"),
            ("src/AcceptanceSpecSystem.Application/Services/DocumentFileAppService.cs",
                "IDocumentFileAppService",
                "DocumentFileAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs"),
            ("src/AcceptanceSpecSystem.Application/Services/DocumentTableQueryAppService.cs",
                "IDocumentTableQueryAppService",
                "DocumentTableQueryAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs"),
            ("src/AcceptanceSpecSystem.Application/Services/MatchingTaskAppService.cs",
                "IMatchingTaskAppService",
                "MatchingTaskAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/MatchingTaskController.cs"),
            ("src/AcceptanceSpecSystem.Application/Services/AuthRoleAppService.cs",
                "IAuthRoleAppService",
                "AuthRoleAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/AuthRolesController.cs"),
            ("src/AcceptanceSpecSystem.Application/Services/OrgUnitAppService.cs",
                "IOrgUnitAppService",
                "OrgUnitAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/OrgUnitsController.cs"),
            ("src/AcceptanceSpecSystem.Application/Services/SystemUserAppService.cs",
                "ISystemUserAppService",
                "SystemUserAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/SystemUsersController.cs"),
            ("src/AcceptanceSpecSystem.Application/Services/ExecutionHistoryAppService.cs",
                "IExecutionHistoryAppService",
                "ExecutionHistoryAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/ExecutionHistoryController.cs"),
            ("src/AcceptanceSpecSystem.Application/Services/BatchReplyAppService.cs",
                "IBatchReplyAppService",
                "BatchReplyAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/BatchReplyController.cs"),
            ("src/AcceptanceSpecSystem.Application/Services/DocumentImportAppService.cs",
                "IDocumentImportAppService",
                "DocumentImportAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs"),
            ("src/AcceptanceSpecSystem.Application/Services/MatchingPreviewAppService.cs",
                "IMatchingPreviewAppService",
                "MatchingPreviewAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/MatchingPreviewController.cs"),
            ("src/AcceptanceSpecSystem.Application/Services/SmartFillSpecBackfillAppService.cs",
                "ISmartFillSpecBackfillAppService",
                "SmartFillSpecBackfillAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/MatchingExecutionController.cs")
        };

        foreach (var (servicePath, interfaceName, implementationName, controllerPath) in services)
        {
            var serviceContent = ReadFile(servicePath);
            serviceContent.Should().Contain($"public interface {interfaceName}",
                $"{implementationName} 应先暴露接口，便于控制器只依赖用例契约");
            serviceContent.Should().MatchRegex($@"public\s+sealed\s+(partial\s+)?class\s+{implementationName}\s*:\s*{interfaceName}",
                $"{implementationName} 应显式实现自身接口");
            serviceCollectionContent.Should().Contain($"AddScoped<{interfaceName}, {implementationName}>()",
                $"{implementationName} 应按接口注册到 DI");

            var controllerContent = ReadFile(controllerPath);
            controllerContent.Should().Contain($"private readonly {interfaceName}",
                $"{controllerPath} 字段应依赖接口");
            var controllerName = Path.GetFileNameWithoutExtension(controllerPath);
            controllerContent.Should().MatchRegex($@"public\s+{controllerName}\s*\([^)]*\b{interfaceName}\s+\w+",
                $"{controllerPath} 构造函数应注入接口");
        }
    }

    [Fact]
    public void SmartConfigurationAppService_ShouldExposeInterface_AndControllerShouldDependOnInterface()
    {
        var serviceContent = ReadFile("src/AcceptanceSpecSystem.Application/Services/SmartConfigurationAppService.cs");
        var serviceCollectionContent = ReadFile("src/AcceptanceSpecSystem.Application/ServiceCollectionExtensions.cs");
        var controllerContent = ReadFile("src/AcceptanceSpecSystem.Api/Controllers/SmartConfigController.cs");

        serviceContent.Should().Contain("public interface ISmartConfigurationAppService",
            "智能结构识别用例也应暴露接口，避免控制器依赖具体实现");
        serviceContent.Should().MatchRegex(
            @"public\s+sealed\s+class\s+SmartConfigurationAppService\s*:\s*ISmartConfigurationAppService",
            "SmartConfigurationAppService 应显式实现自身接口");
        serviceCollectionContent.Should().Contain("AddScoped<ISmartConfigurationAppService, SmartConfigurationAppService>()",
            "SmartConfigurationAppService 应按接口注册到 Application DI");
        controllerContent.Should().Contain("private readonly ISmartConfigurationAppService",
            "SmartConfigController 字段应依赖接口");
        controllerContent.Should().MatchRegex(
            @"public\s+SmartConfigController\s*\([^)]*\bISmartConfigurationAppService\s+\w+",
            "SmartConfigController 构造函数应注入接口");
    }

    [Fact]
    public void SmartConfiguration_ShouldNotKeepArchivedAutoDetectEndpoint()
    {
        var controllerContent = ReadFile("src/AcceptanceSpecSystem.Api/Controllers/SmartConfigController.cs");
        var appServiceContent = ReadFile("src/AcceptanceSpecSystem.Application/Services/SmartConfigurationAppService.cs");
        var intelligenceServiceContent = ReadFile("src/AcceptanceSpecSystem.Core/Documents/Intelligence/IDocumentIntelligenceService.cs");

        controllerContent.Should().NotContain("auto-detect", "智能结构识别公开入口已统一为 recognize");
        controllerContent.Should().NotContain("AutoDetect", "控制器不应保留归档自动配置 Action");
        controllerContent.Should().NotContain("AutoDetectRequest", "归档请求 DTO 应随旧入口删除");
        appServiceContent.Should().NotContain("AutoConfigureAsync", "应用服务不应保留归档单表自动配置入口");
        intelligenceServiceContent.Should().NotContain("AutoConfigureAsync", "Core 智能识别接口不应保留归档单表自动配置入口");
    }

    [Fact]
    public void SmartConfigurationRecognizedTableDtoMapping_ShouldBeCentralizedInFactory()
    {
        var repositoryRoot = GetRepositoryRoot();
        var factoryPath = Path.Combine(
            repositoryRoot,
            "src/AcceptanceSpecSystem.Application/Services/SmartConfigurationRecognizedTableFactory.cs".Replace('/', Path.DirectorySeparatorChar));

        File.Exists(factoryPath).Should().BeTrue("智能结构识别 DTO 构造边界应集中在专用工厂");

        var factoryContent = File.ReadAllText(factoryPath);
        factoryContent.Should().Contain("SmartConfigurationRecognizedTableFactory");
        factoryContent.Should().Contain("SmartConfigurationTableStructure",
            "智能结构识别内部列映射应先收敛到统一结构，再转换为 API DTO");
        factoryContent.Should().Contain("FromTemplate");
        factoryContent.Should().Contain("FromMapping");
        factoryContent.Should().Contain("FromCandidate");
        factoryContent.Should().Contain("ToStructureCandidate");
        factoryContent.Should().Contain("ToColumnMappingResult");
        factoryContent.Should().Contain("ToRecognizedTable");

        var appServiceContent = ReadFile("src/AcceptanceSpecSystem.Application/Services/SmartConfigurationAppService.cs");
        appServiceContent.Should().Contain("SmartConfigurationRecognizedTableFactory");
        appServiceContent.Should().NotContain("BuildRecognizedTableFromTemplate(",
            "AppService 应只负责编排，不应继续散落模板识别 DTO 构造");
        appServiceContent.Should().NotContain("BuildRecognizedTableFromMapping(",
            "AppService 应只负责编排，不应继续散落规则识别 DTO 构造");
        appServiceContent.Should().NotContain("BuildRecognizedTableFromCandidate(",
            "AppService 应只负责编排，不应继续散落融合识别 DTO 构造");
        appServiceContent.Should().NotContain("private static DocumentStructureCandidate ToStructureCandidate",
            "规则结果到结构候选的转换应收敛到工厂");
        appServiceContent.Should().NotContain("private static ColumnMappingResult ToColumnMappingResult",
            "结构候选到列映射结果的转换应收敛到工厂");
    }

    [Fact]
    public void SmartConfigurationRecognizedTableCopies_ShouldUseRecordWithExpressions()
    {
        var modelContent = ReadFile("src/AcceptanceSpecSystem.Application/Services/SmartConfigurationRecognizeModels.cs");
        var appServiceContent = ReadFile("src/AcceptanceSpecSystem.Application/Services/SmartConfigurationAppService.cs");
        var routingContent = ReadFile("src/AcceptanceSpecSystem.Application/Services/SmartConfigurationTableRoutingService.cs");

        modelContent.Should().Contain("public sealed record SmartConfigurationRecognizedTable",
            "响应 DTO 需要依靠 record 复制构造器自动保留未来新增字段");
        appServiceContent.Should().MatchRegex(
            @"CopyWithSemanticRecallSuggestions\([\s\S]*?return\s+table\s+with\s*\{",
            "语义召回复制只应覆盖变化字段");
        routingContent.Should().MatchRegex(
            @"CopyWithRouting\([\s\S]*?return\s+table\s+with\s*\{",
            "路由复制只应覆盖变化字段");
    }

    [Fact]
    public void SmartConfigRecognizeApiTests_ShouldBeSplitByResponsibility()
    {
        var testDirectory = Path.Combine(GetRepositoryRoot(), "tests", "AcceptanceSpecSystem.Api.Tests");
        var expectedFiles = new[]
        {
            "SmartConfigRecognizeApiTests.cs",
            "SmartConfigRecognizeHealthAndFusionApiTests.cs",
            "SmartConfigRecognizeHistoryApiTests.cs",
            "SmartConfigRecognizeConfirmationApiTests.cs",
            "SmartConfigRecognizeHeaderApiTests.cs",
            "SmartConfigRecognizeLlmBudgetApiTests.cs",
            "SmartConfigRecognizeColumnSemanticRecallApiTests.cs",
            "SmartConfigRecognizeApiFactories.cs",
            "SmartConfigRecognizeLlmTestDoubles.cs",
            "SmartConfigRecognizeIntelligenceTestDoubles.cs",
            "SmartConfigRecognizeTestFiles.cs"
        };
        var files = Directory.GetFiles(testDirectory, "SmartConfigRecognize*.cs");
        var fileNames = files.Select(Path.GetFileName).ToList();

        foreach (var expectedFile in expectedFiles)
        {
            fileNames.Should().Contain(expectedFile, "智能识别测试应按场景和设施职责拆分");
        }

        foreach (var file in files)
        {
            File.ReadLines(file).Count().Should().BeLessThanOrEqualTo(
                800,
                $"{Path.GetFileName(file)} 不应重新膨胀为大型测试文件");
        }

        foreach (var scenarioFile in files.Where(file => Path.GetFileName(file).EndsWith("ApiTests.cs")))
        {
            var content = File.ReadAllText(scenarioFile);
            content.Should().NotContain("new MultipartFormDataContent",
                $"{Path.GetFileName(scenarioFile)} 应复用统一上传 helper");
            content.Should().NotContain("WordprocessingDocument.Create",
                $"{Path.GetFileName(scenarioFile)} 应复用统一 Word 测试文档 helper");
        }

        File.ReadAllText(Path.Combine(testDirectory, "SmartConfigRecognizeTestFiles.cs"))
            .Should().Contain("UploadExcelAsync", "重复文件上传应收敛到无状态 helper");
        var factoryContent = File.ReadAllText(Path.Combine(testDirectory, "SmartConfigRecognizeApiFactories.cs"));
        factoryContent.Should().Contain("SmartConfigRecognizeApiFactoryBase",
            "具名 Factory 应复用统一装配骨架");
        factoryContent.Should().Contain("ReplaceScoped<",
            "测试服务替换应收敛到统一 DI helper");
    }

    [Fact]
    public void PermissionAndNavigationMetadata_ShouldUseSharedManifest_AndNotDependOnAsyncRoutesRuntime()
    {
        var repositoryRoot = GetRepositoryRoot();
        File.Exists(Path.Combine(repositoryRoot, "shared/navigation/navigation-manifest.json"))
            .Should().BeTrue("页面、菜单、权限码应收敛到共享导航清单");

        var seedContent = ReadFile("src/AcceptanceSpecSystem.Application/Services/AuthUserSeedAppService.cs");
        seedContent.Should().NotContain("PagePermissions =", "后端权限种子不应继续内嵌页面权限数组");
        seedContent.Should().NotContain("MenuPermissions =", "后端权限种子不应继续内嵌菜单权限数组");
        seedContent.Should().Contain("IAuthPermissionSeedCatalog", "Application 种子服务应通过宿主端口获取权限定义");
        seedContent.Should().NotContain("Microsoft.AspNetCore", "Application 种子服务不应依赖 ASP.NET 元数据");

        var seedCatalogContent = ReadFile("src/AcceptanceSpecSystem.Api/Services/AuthPermissionSeedCatalog.cs");
        seedCatalogContent.Should().Contain("navigation-manifest.json", "API 宿主适配器应消费共享导航清单");
        seedCatalogContent.Should().Contain("HttpMethodAttribute", "API 宿主适配器负责读取 ASP.NET 动作元数据");

        var routerUtilsContent = ReadFile("web/src/router/utils.ts");
        routerUtilsContent.Should().NotContain("getAsyncRoutes", "前端启动不应再依赖运行时 async-routes 接口");
        routerUtilsContent.Should().NotContain("asyncRoutesStorageKey", "前端不应再维护 async-routes 本地缓存");

        var userStoreContent = ReadFile("web/src/store/modules/user.ts");
        userStoreContent.Should().NotContain("\"async-routes\"", "登出和刷新流程不应再处理 async-routes 缓存");

        var routesApiContent = ReadFile("web/src/api/routes.ts");
        routesApiContent.Should().NotContain("getAsyncRoutes", "前端不应再保留 async-routes 空壳 API");

        var authControllerContent = ReadFile("src/AcceptanceSpecSystem.Api/Controllers/AuthController.cs");
        authControllerContent.Should().NotContain("get-async-routes", "后端不应再保留 async-routes 兼容端点");

        var permissionConventionsContent = ReadFile("src/AcceptanceSpecSystem.Api/Authorization/PermissionConventions.cs");
        permissionConventionsContent.Should().NotContain("get-async-routes", "权限约定不应继续为已删除端点保留专用动作映射");

        var viteConfigContent = ReadFile("web/vite.config.ts");
        viteConfigContent.Should().NotContain("\"/get-async-routes\"", "Vite 代理不应继续保留 async-routes 空壳链路");
    }

    [Fact]
    public void Frontend_ShouldNotRegisterUnusedAuthOrPermsComponentsGlobally()
    {
        var mainContent = ReadFile("web/src/main.ts");
        mainContent.Should().NotContain("components/ReAuth", "全局 Auth 组件已无消费，不应继续注册");
        mainContent.Should().NotContain("components/RePerms", "全局 Perms 组件已无消费，不应继续注册");
        mainContent.Should().NotContain("app.component(\"Auth\"", "全局 Auth 组件已无消费，不应继续注册");
        mainContent.Should().NotContain("app.component(\"Perms\"", "全局 Perms 组件已无消费，不应继续注册");
    }

    [Fact]
    public void MatchingDefaultRecallTopKQueries_ShouldIgnoreDisabledEmbeddingServices()
    {
        var resolverContent = ReadFile("src/AcceptanceSpecSystem.Application/Services/MatchingConfigResolver.cs");

        resolverContent.Should().Contain("!item.IsDisabled");
    }

    [Fact]
    public void Controllers_ShouldPassCancellationTokenToEfAsyncQueries()
    {
        var repositoryRoot = GetRepositoryRoot();
        var controllerFiles = Directory.GetFiles(
            Path.Combine(repositoryRoot, "src", "AcceptanceSpecSystem.Api", "Controllers"),
            "*.cs");

        foreach (var controllerFile in controllerFiles)
        {
            var content = File.ReadAllText(controllerFile);
            content.Should().NotContain(".CountAsync();", $"{Path.GetFileName(controllerFile)} 查询计数应透传请求取消令牌");
            content.Should().NotContain(".ToListAsync();", $"{Path.GetFileName(controllerFile)} 查询列表应透传请求取消令牌");
        }
    }

    [Fact]
    public void AiServicesQueryEndpoints_ShouldDelegateCancellableQueriesToApplication()
    {
        var controller = ReadFile("src/AcceptanceSpecSystem.Api/Controllers/AiServicesController.cs");
        var appService = ReadFile("src/AcceptanceSpecSystem.Application/Services/AiServiceConfigurationAppService.cs");

        controller.Should().Contain("IAiServiceConfigurationAppService");
        controller.Should().Contain("GetProbeConfigAsync(id, cancellationToken)");
        controller.Should().Contain("ProbeModelsAsync(entity, cancellationToken)");
        controller.Should().NotContain("IUnitOfWork");
        appService.Should().Contain("SingleOrDefaultAsync(config => config.Id == id, cancellationToken)");
    }

    [Fact]
    public void MatchingUseCases_ShouldShareMatchingConfigResolver()
    {
        var previewContent = ReadFile("src/AcceptanceSpecSystem.Application/Services/MatchingPreviewAppService.cs");
        var workflowContent = ReadServiceFamily("MatchingWorkflow");
        var programContent = string.Join(Environment.NewLine,
            ReadFile("src/AcceptanceSpecSystem.Api/ServiceCollectionExtensions.cs"),
            ReadFile("src/AcceptanceSpecSystem.Application/ServiceCollectionExtensions.cs"));

        previewContent.Should().Contain("MatchingConfigResolver");
        workflowContent.Should().Contain("MatchingConfigResolver");
        programContent.Should().Contain("AddScoped<MatchingConfigResolver>()");

        previewContent.Should().NotContain("ConvertToMatchingConfigAsync",
            "匹配配置归一化应收敛到共享解析器，避免预览与执行路径双写后漂移");
        workflowContent.Should().NotContain("ConvertToMatchingConfigAsync",
            "匹配配置归一化应收敛到共享解析器，避免预览与执行路径双写后漂移");
        previewContent.Should().NotContain("ResolveDefaultRecallTopKAsync",
            "默认召回数量查询应只保留一份");
        workflowContent.Should().NotContain("ResolveDefaultRecallTopKAsync",
            "默认召回数量查询应只保留一份");
    }

    [Fact]
    public void MatchingUseCases_ShouldShareResultDtoMapper()
    {
        var mapperContent = ReadFile("src/AcceptanceSpecSystem.Application/Services/MatchingResultDtoMapper.cs");
        var previewContent = ReadFile("src/AcceptanceSpecSystem.Application/Services/MatchingPreviewAppService.cs");
        var workflowContent = ReadServiceFamily("MatchingWorkflow");

        mapperContent.Should().Contain("ToMatchResultDto");
        previewContent.Should().Contain("MatchingResultDtoMapper");
        workflowContent.Should().Contain("MatchingResultDtoMapper");

        previewContent.Should().NotContain("ConvertToMatchResultDto");
        workflowContent.Should().NotContain("ConvertToMatchResultDto");
        previewContent.Should().NotContain("ConvertToIssueDto");
        workflowContent.Should().NotContain("ConvertToIssueDto");
        previewContent.Should().NotContain("ToEvidenceRelationKey");
        workflowContent.Should().NotContain("ToEvidenceRelationKey");
    }

    [Fact]
    public void MatchingUseCases_ShouldShareCandidateProvider()
    {
        var providerContent = ReadFile("src/AcceptanceSpecSystem.Application/Services/MatchingCandidateProvider.cs");
        var previewContent = ReadFile("src/AcceptanceSpecSystem.Application/Services/MatchingPreviewAppService.cs");
        var workflowContent = ReadServiceFamily("MatchingWorkflow");
        var programContent = string.Join(Environment.NewLine,
            ReadFile("src/AcceptanceSpecSystem.Api/ServiceCollectionExtensions.cs"),
            ReadFile("src/AcceptanceSpecSystem.Application/ServiceCollectionExtensions.cs"));

        providerContent.Should().Contain("GetCandidatesAsync");
        providerContent.Should().Contain("MaxScopedCandidateCount");
        providerContent.Should().Contain("BuildCandidateDedupKey");
        programContent.Should().Contain("AddScoped<MatchingCandidateProvider>()");
        previewContent.Should().Contain("MatchingCandidateProvider");
        workflowContent.Should().Contain("MatchingCandidateProvider");

        previewContent.Should().NotContain("private async Task<List<MatchCandidate>> GetCandidatesAsync");
        workflowContent.Should().NotContain("private async Task<List<MatchCandidate>> GetCandidatesAsync");
        previewContent.Should().NotContain("BuildCandidateSpecQuery");
        workflowContent.Should().NotContain("BuildCandidateSpecQuery");
        previewContent.Should().NotContain("ApplySpecScopeToQuery");
        workflowContent.Should().NotContain("ApplySpecScopeToQuery");
    }

    [Fact]
    public void ExactMatchOnly_ShouldNotRequireEmbeddingService()
    {
        var previewContent = ReadFile("src/AcceptanceSpecSystem.Application/Services/MatchingPreviewAppService.cs");
        var workflowContent = ReadServiceFamily("MatchingWorkflow");

        previewContent.Should().NotMatchRegex(
            @"if\s*\(\s*config\.ExactMatchOnly\s*\)\s*\{\s*await EnsureEmbeddingServiceConfiguredAsync",
            "仅精确匹配只比较项目+规格文本，不应依赖 Embedding 服务");
        workflowContent.Should().NotMatchRegex(
            @"if\s*\(\s*config\.ExactMatchOnly\s*\)\s*\{\s*await EnsureEmbeddingServiceConfiguredAsync",
            "仅精确匹配只比较项目+规格文本，不应依赖 Embedding 服务");
    }

    [Fact]
    public void SmartStructureRoutingRegex_ShouldUseExplicitTimeout()
    {
        var routingContent = ReadFile("src/AcceptanceSpecSystem.Application/Services/SmartConfigurationTableRoutingService.cs");
        var configurationContent = ReadFile("src/AcceptanceSpecSystem.Application/Services/SmartStructureRoutingRuleAppService.cs");

        routingContent.Should().Contain("RegexMatchTimeoutException");
        routingContent.Should().Contain("Regex.IsMatch(");
        routingContent.Should().Contain("RegexMatchTimeout");
        configurationContent.Should().Contain("RegexMatchTimeout");
    }

    [Fact]
    public void ConfigurationAndAuditProtocolAdapters_ShouldOnlyDependOnApplicationPorts()
    {
        var adapters = new[]
        {
            "src/AcceptanceSpecSystem.Api/Controllers/AuditOperationAttribute.cs",
            "src/AcceptanceSpecSystem.Api/Controllers/AuditLogsController.cs",
            "src/AcceptanceSpecSystem.Api/Controllers/AiServicesController.cs",
            "src/AcceptanceSpecSystem.Api/Controllers/ColumnMappingRulesController.cs",
            "src/AcceptanceSpecSystem.Api/Controllers/PromptTemplatesController.cs",
            "src/AcceptanceSpecSystem.Api/Controllers/SmartStructureRoutingRulesController.cs"
        };

        foreach (var adapter in adapters)
        {
            var content = ReadFile(adapter);
            content.Should().NotContain("IUnitOfWork", $"{adapter} 不应直接编排工作单元");
            content.Should().NotContain("AppDbContext", $"{adapter} 不应直接访问 DbContext");
            Regex.IsMatch(content, @"\bI[A-Za-z0-9]+Repository\b").Should().BeFalse(
                $"{adapter} 不应直接访问 Repository");
            content.Should().NotContain("SaveChangesAsync", $"{adapter} 不应拥有事务提交边界");
        }

        ReadFile("src/AcceptanceSpecSystem.Api/Controllers/AuditOperationAttribute.cs")
            .Should().Contain("IAuditTrailAppService");
        ReadFile("src/AcceptanceSpecSystem.Api/Controllers/ColumnMappingRulesController.cs")
            .Should().Contain("IColumnMappingRuleAppService");
        ReadFile("src/AcceptanceSpecSystem.Api/Controllers/PromptTemplatesController.cs")
            .Should().Contain("IPromptTemplateAppService");
        ReadFile("src/AcceptanceSpecSystem.Api/Controllers/SmartStructureRoutingRulesController.cs")
            .Should().Contain("ISmartStructureRoutingRuleAppService");

        foreach (var legacyDto in new[]
                 {
                     "AuditLogDtos.cs", "AiServiceDtos.cs", "ColumnMappingRuleDtos.cs",
                     "PromptTemplateDtos.cs", "SmartStructureRoutingRuleDtos.cs"
                 })
        {
            File.Exists(Path.Combine(GetRepositoryRoot(), "src", "AcceptanceSpecSystem.Api", "DTOs", legacyDto))
                .Should().BeFalse($"{legacyDto} 已迁入 Application 契约所有权");
        }
    }

    [Fact]
    public void BackendUseCaseServiceFiles_ShouldStayBelowLargeFileThreshold()
    {
        var serviceFiles = Directory.GetFiles(
            Path.Combine(GetRepositoryRoot(), "src", "AcceptanceSpecSystem.Application", "Services"),
            "*.cs")
            .Where(path =>
            {
                var fileName = Path.GetFileName(path);
                return fileName.StartsWith("MatchingWorkflow", StringComparison.Ordinal) ||
                       fileName.StartsWith("BatchReplyAppService", StringComparison.Ordinal) ||
                       fileName.StartsWith("DocumentImportAppService", StringComparison.Ordinal);
            })
            .ToList();

        serviceFiles.Should().NotBeEmpty("后端核心用例服务应按职责拆成多个小文件");

        foreach (var serviceFile in serviceFiles)
        {
            var lineCount = File.ReadLines(serviceFile).Count();
            lineCount.Should().BeLessThan(500, $"{Path.GetFileName(serviceFile)} 应保持单一职责，避免重新膨胀为巨型文件");
        }
    }

    [Fact]
    public void CoreMatchingServiceFiles_ShouldNotGrowBeyondCurrentLargeFileBaseline()
    {
        var serviceDirectory = Path.Combine(GetRepositoryRoot(), "src", "AcceptanceSpecSystem.Core", "Matching", "Services");
        var serviceFiles = Directory.GetFiles(serviceDirectory, "*.cs");
        serviceFiles.Should().NotBeEmpty("Core Matching 服务目录应存在可治理文件");

        foreach (var serviceFile in serviceFiles)
        {
            var fileName = Path.GetFileName(serviceFile);
            var lineCount = File.ReadLines(serviceFile).Count();
            lineCount.Should().BeLessThan(
                500,
                $"{fileName} 应保持单一职责，避免形成 Core Matching 巨型文件");
        }
    }

    private static string ReadFile(string relativePath)
    {
        return File.ReadAllText(Path.Combine(GetRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string ReadServiceFamily(string fileNamePrefix)
    {
        var serviceDirectory = Path.Combine(GetRepositoryRoot(), "src", "AcceptanceSpecSystem.Application", "Services");
        var contents = Directory.GetFiles(serviceDirectory, $"{fileNamePrefix}*.cs")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Select(File.ReadAllText);

        return string.Join(Environment.NewLine, contents);
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AcceptanceSpecSystem.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("未找到仓库根目录");
    }
}
