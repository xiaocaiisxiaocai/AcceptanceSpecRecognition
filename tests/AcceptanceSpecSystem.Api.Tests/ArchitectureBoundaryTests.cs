using System.Reflection;
using System.Text.RegularExpressions;
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
        var content = ReadFile("src/AcceptanceSpecSystem.Api/Services/MatchingPreviewAppService.cs");

        content.Should().NotContain("MatchingWorkflowService", "预览应用服务应承载独立预览用例，而不是继续透传巨型工作流服务");
    }

    [Fact]
    public void MatchingWorkflowService_ShouldNotKeepPreviewEntrypoints()
    {
        var content = ReadFile("src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs");

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
        var content = ReadFile("src/AcceptanceSpecSystem.Api/Services/MatchingTaskAppService.cs");

        content.Should().NotContain("MatchingWorkflowService", "任务下载应用服务应承载独立下载用例，而不是继续透传巨型工作流服务");
    }

    [Fact]
    public void MatchingExecutionAppService_ShouldDelegateToSplitUseCaseServices()
    {
        var content = ReadFile("src/AcceptanceSpecSystem.Api/Services/MatchingExecutionAppService.cs");

        content.Should().NotContain("MatchingWorkflowService",
            "执行应用服务不应继续直接透传巨型工作流服务");
        content.Should().Contain("MatchingLlmStreamAppService",
            "LLM 流式复核应拆到独立用例服务");
        content.Should().Contain("MatchingFillExecutionAppService",
            "执行填充与批量执行应拆到独立用例服务");
    }

    [Fact]
    public void MatchingWorkflowService_ShouldNotKeepTaskDownloadEntrypoint()
    {
        var content = ReadFile("src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs");

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
        var content = ReadFile("src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs");

        content.Should().NotContain("public async Task<MatchingOperationResult<StrictReusePreviewResponse>> PreviewStrictReuseAsync(",
            "严格复用预检入口已经拆到独立应用服务，不应继续留在巨型工作流服务中");
        content.Should().NotContain("public async Task<MatchingOperationResult<StrictReuseExecuteResponse>> ExecuteStrictReuseAsync(",
            "严格复用执行入口已经拆到独立应用服务，不应继续留在巨型工作流服务中");
    }

    [Fact]
    public void MatchingWorkflowService_ShouldNotKeepExecutionOrSnapshotEntrypoints()
    {
        var content = ReadFile("src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs");

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
            "src/AcceptanceSpecSystem.Api/Services/MatchingTaskSnapshotService.cs".Replace('/', Path.DirectorySeparatorChar));

        File.Exists(snapshotServicePath).Should().BeTrue("任务快照应收敛到独立共享服务，供执行与下载共同使用");

        var snapshotContent = File.ReadAllText(snapshotServicePath);
        snapshotContent.Should().Contain("SaveAsync(", "快照服务应提供统一保存入口");
        snapshotContent.Should().Contain("LoadAsync(", "快照服务应提供统一读取入口");
        snapshotContent.Should().Contain("EnsureTaskOwnership", "快照服务应统一校验任务归属");

        ReadFile("src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs")
            .Should().Contain("MatchingTaskSnapshotService",
                "执行填充核心协作组件应通过共享快照服务持久化任务结果");
        ReadFile("src/AcceptanceSpecSystem.Api/Services/MatchingTaskAppService.cs")
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

        ReadFile("src/AcceptanceSpecSystem.Api/Services/DocumentImportAppService.cs")
            .Should().Contain("DocumentFileAccessService",
                "导入应用服务应复用共享文件读取组件");
        ReadFile("src/AcceptanceSpecSystem.Api/Services/MatchingPreviewAppService.cs")
            .Should().Contain("DocumentTableAccessService",
                "匹配预览应用服务应复用共享表格提取组件");
        ReadFile("src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs")
            .Should().Contain("MatchingResultWriteBackService",
                "执行填充协作组件应复用共享结果写回组件");
        ReadFile("src/AcceptanceSpecSystem.Api/Services/MatchingTaskAppService.cs")
            .Should().Contain("MatchingResultWriteBackService",
                "下载应用服务应复用共享结果写回组件");
        ReadFile("src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs")
            .Should().Contain("DocumentTableAccessService",
                "文档控制器相关用例应通过共享表格访问组件完成");
    }

    [Fact]
    public void DocumentsController_ShouldDelegateDocumentResourceUseCases_ToApplicationServices()
    {
        var content = ReadFile("src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs");

        content.Should().Contain("DocumentFileAppService",
            "文档资源接口应通过独立应用服务完成文件列表、上传、预览与删除编排");
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
        content.Should().NotContain("GetTablesAsync(",
            "文档控制器不应再直接调用表格查询实现");
        content.Should().NotContain("DeleteIfExistsAsync(",
            "文档控制器不应再直接删除物理文件");
    }

    [Fact]
    public void SimpleApiAppServices_ShouldExposeInterfaces_AndControllersShouldDependOnInterfaces()
    {
        var serviceCollectionContent = ReadFile("src/AcceptanceSpecSystem.Api/ServiceCollectionExtensions.cs");

        var services = new[]
        {
            ("src/AcceptanceSpecSystem.Api/Services/DashboardAppService.cs",
                "IDashboardAppService",
                "DashboardAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/DashboardController.cs"),
            ("src/AcceptanceSpecSystem.Api/Services/DocumentFileAppService.cs",
                "IDocumentFileAppService",
                "DocumentFileAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs"),
            ("src/AcceptanceSpecSystem.Api/Services/MatchingTaskAppService.cs",
                "IMatchingTaskAppService",
                "MatchingTaskAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/MatchingTaskController.cs"),
            ("src/AcceptanceSpecSystem.Api/Services/AuthRoleAppService.cs",
                "IAuthRoleAppService",
                "AuthRoleAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/AuthRolesController.cs"),
            ("src/AcceptanceSpecSystem.Api/Services/OrgUnitAppService.cs",
                "IOrgUnitAppService",
                "OrgUnitAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/OrgUnitsController.cs"),
            ("src/AcceptanceSpecSystem.Api/Services/SystemUserAppService.cs",
                "ISystemUserAppService",
                "SystemUserAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/SystemUsersController.cs"),
            ("src/AcceptanceSpecSystem.Api/Services/ExecutionHistoryAppService.cs",
                "IExecutionHistoryAppService",
                "ExecutionHistoryAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/ExecutionHistoryController.cs"),
            ("src/AcceptanceSpecSystem.Api/Services/BatchReplyAppService.cs",
                "IBatchReplyAppService",
                "BatchReplyAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/BatchReplyController.cs"),
            ("src/AcceptanceSpecSystem.Api/Services/DocumentImportAppService.cs",
                "IDocumentImportAppService",
                "DocumentImportAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs"),
            ("src/AcceptanceSpecSystem.Api/Services/MatchingPreviewAppService.cs",
                "IMatchingPreviewAppService",
                "MatchingPreviewAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/MatchingPreviewController.cs"),
            ("src/AcceptanceSpecSystem.Api/Services/MatchingExecutionAppService.cs",
                "IMatchingExecutionAppService",
                "MatchingExecutionAppService",
                "src/AcceptanceSpecSystem.Api/Controllers/MatchingExecutionController.cs"),
            ("src/AcceptanceSpecSystem.Api/Services/SmartFillSpecBackfillAppService.cs",
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
    public void PermissionAndNavigationMetadata_ShouldUseSharedManifest_AndNotDependOnAsyncRoutesRuntime()
    {
        var repositoryRoot = GetRepositoryRoot();
        File.Exists(Path.Combine(repositoryRoot, "shared/navigation/navigation-manifest.json"))
            .Should().BeTrue("页面、菜单、权限码应收敛到共享导航清单");

        var seedContent = ReadFile("src/AcceptanceSpecSystem.Api/Services/AuthUserSeedService.cs");
        seedContent.Should().NotContain("PagePermissions =", "后端权限种子不应继续内嵌页面权限数组");
        seedContent.Should().NotContain("MenuPermissions =", "后端权限种子不应继续内嵌菜单权限数组");
        seedContent.Should().Contain("navigation-manifest.json", "后端权限种子应消费共享导航清单");

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
        var resolverContent = ReadFile("src/AcceptanceSpecSystem.Api/Services/MatchingConfigResolver.cs");

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
    public void AiServicesQueryEndpoints_ShouldUseCancellableEfQueries()
    {
        var content = ReadFile("src/AcceptanceSpecSystem.Api/Controllers/AiServicesController.cs");

        content.Should().Contain("GetById(");
        content.Should().Contain("CancellationToken cancellationToken = default");
        content.Should().Contain(".SingleOrDefaultAsync(config => config.Id == id, cancellationToken)");
        content.Should().Contain("GetModels(");
        content.Should().Contain("ProbeModelsAsync(entity, cancellationToken)");
    }

    [Fact]
    public void MatchingUseCases_ShouldShareMatchingConfigResolver()
    {
        var previewContent = ReadFile("src/AcceptanceSpecSystem.Api/Services/MatchingPreviewAppService.cs");
        var workflowContent = ReadServiceFamily("MatchingWorkflow");
        var programContent = ReadFile("src/AcceptanceSpecSystem.Api/ServiceCollectionExtensions.cs");

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
        var mapperContent = ReadFile("src/AcceptanceSpecSystem.Api/Services/MatchingResultDtoMapper.cs");
        var previewContent = ReadFile("src/AcceptanceSpecSystem.Api/Services/MatchingPreviewAppService.cs");
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
        var providerContent = ReadFile("src/AcceptanceSpecSystem.Api/Services/MatchingCandidateProvider.cs");
        var previewContent = ReadFile("src/AcceptanceSpecSystem.Api/Services/MatchingPreviewAppService.cs");
        var workflowContent = ReadServiceFamily("MatchingWorkflow");
        var programContent = ReadFile("src/AcceptanceSpecSystem.Api/ServiceCollectionExtensions.cs");

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
        var previewContent = ReadFile("src/AcceptanceSpecSystem.Api/Services/MatchingPreviewAppService.cs");
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
        var controllerContent = ReadFile("src/AcceptanceSpecSystem.Api/Controllers/SmartStructureRoutingRulesController.cs");

        routingContent.Should().Contain("RegexMatchTimeoutException");
        routingContent.Should().Contain("Regex.IsMatch(");
        routingContent.Should().Contain("RegexMatchTimeout");
        controllerContent.Should().Contain("RegexMatchTimeout");
    }

    [Fact]
    public void BackendUseCaseServiceFiles_ShouldStayBelowLargeFileThreshold()
    {
        var serviceFiles = Directory.GetFiles(
            Path.Combine(GetRepositoryRoot(), "src", "AcceptanceSpecSystem.Api", "Services"),
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
        var serviceDirectory = Path.Combine(GetRepositoryRoot(), "src", "AcceptanceSpecSystem.Api", "Services");
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
