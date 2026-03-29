using System.Reflection;
using AcceptanceSpecSystem.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Tests;

public class ReviewRegressionTests
{
    [Fact]
    public void SpecsController_DuplicateGroups_ShouldNotLoadAllSpecsIntoMemory()
    {
        var lines = ReadFile("src/AcceptanceSpecSystem.Api/Controllers/SpecsController.cs");

        lines.Should().NotContain(line =>
            line.Contains("GetAllWithCustomerAndProcessAsync()", StringComparison.Ordinal) &&
            line.Contains("AcceptanceSpecs", StringComparison.Ordinal),
            "重复分组接口应将作用域和筛选下推到数据库，而不是先全表加载");
    }

    [Fact]
    public void ProcessesController_GetProcessSpecs_ShouldNotLoadAllSpecsIntoMemory()
    {
        var lines = ReadFile("src/AcceptanceSpecSystem.Api/Controllers/ProcessesController.cs");

        lines.Should().NotContain(line =>
            line.Contains("GetAllWithCustomerAndProcessAsync()", StringComparison.Ordinal) &&
            line.Contains("AcceptanceSpecs", StringComparison.Ordinal),
            "按制程查询规格应复用数据库分页查询，而不是先全表加载");
    }

    [Fact]
    public void AiServicesController_ShouldDependOnHttpClientFactory()
    {
        var constructors = typeof(AcceptanceSpecSystem.Api.Controllers.AiServicesController)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        constructors.Should().ContainSingle();
        constructors[0]
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Should()
            .Contain(typeof(IHttpClientFactory), "AI 服务探测应通过 IHttpClientFactory 创建 HttpClient");
    }

    [Fact]
    public void AcceptanceSpecRepository_ShouldNotExposeLegacyGroupSummaryMethod()
    {
        typeof(AcceptanceSpecSystem.Data.Repositories.IAcceptanceSpecRepository)
            .GetMethod("GetGroupSummaryAsync")
            .Should()
            .BeNull("旧的无作用域分组接口已被新查询接口替代，不应继续暴露");

        typeof(AcceptanceSpecSystem.Data.Repositories.AcceptanceSpecRepository)
            .GetMethod("GetGroupSummaryAsync")
            .Should()
            .BeNull("仓储实现中的旧分组方法应与接口一起删除，避免形成陈旧 API");
    }

    [Fact]
    public void SourceFiles_ShouldNotContainLegacyDefaultPasswords()
    {
        var sourceFiles = new[]
        {
            "src/AcceptanceSpecSystem.Api/Services/AuthUserSeedService.cs",
            "web/src/views/login/index.vue"
        };

        foreach (var relativePath in sourceFiles)
        {
            var content = File.ReadAllText(Path.Combine(GetRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
            content.Should().NotContain("Admin@123456", $"{relativePath} 不应再硬编码默认管理员密码");
            content.Should().NotContain("Common@123456", $"{relativePath} 不应再硬编码默认普通用户密码");
        }
    }

    [Fact]
    public void ScoreDetailDialog_ShouldClearInlineDiffCache()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailDialog.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("inlineDiffCache.clear()", "匹配详情弹窗关闭或切换数据时应清理 diff 缓存");
    }

    [Fact]
    public void ScoreDetailDialog_ShouldHighlightSourceVsBestMatchDifference()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailDialog.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("sourceBestRows", "匹配详情应直接标出源项与最佳匹配的差异位置");
        content.Should().Contain("源项与最佳匹配差异", "应提供专门的差异高亮区块，避免用户自行肉眼比对");
        content.Should().Contain("v-html=\"row.leftHtml\"", "差异区块应复用现有 inline diff 高亮渲染");
        content.Should().Contain("v-html=\"row.rightHtml\"", "差异区块应同时渲染源项与最佳匹配的高亮结果");
    }

    [Fact]
    public void UploadControllers_ShouldPropagateRequestAbortedToFileOperations()
    {
        var documentsContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs".Replace('/', Path.DirectorySeparatorChar)));
        documentsContent.Should().Contain("await file.CopyToAsync(memoryStream, cancellationToken);");
        documentsContent.Should().Contain("SaveUploadedExcelAsync(file.FileName, fileContent, cancellationToken)");
        documentsContent.Should().Contain("SaveUploadedWordAsync(file.FileName, fileContent, cancellationToken)");

        var compareContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Controllers/FileCompareController.cs".Replace('/', Path.DirectorySeparatorChar)));
        compareContent.Should().Contain("await file.CopyToAsync(memoryStream, cancellationToken);");
        compareContent.Should().Contain("SaveUploadedExcelAsync(existingFile.FileName, fileContent, cancellationToken)");
        compareContent.Should().Contain("SaveUploadedWordAsync(existingFile.FileName, fileContent, cancellationToken)");
        compareContent.Should().Contain("SaveUploadedExcelAsync(file.FileName, fileContent, cancellationToken)");
        compareContent.Should().Contain("SaveUploadedWordAsync(file.FileName, fileContent, cancellationToken)");
    }

    [Fact]
    public void MatchingEndpoints_ShouldBeSplitIntoFocusedControllers()
    {
        var controllerTypes = typeof(BaseApiController).Assembly
            .GetTypes()
            .Where(type =>
                !type.IsAbstract &&
                typeof(ControllerBase).IsAssignableFrom(type) &&
                type.Namespace == typeof(BaseApiController).Namespace &&
                type.Name.StartsWith("Matching", StringComparison.Ordinal))
            .ToList();

        controllerTypes.Select(type => type.Name).Should().Contain([
            "MatchingPreviewController",
            "MatchingExecutionController",
            "MatchingTaskController",
            "MatchingReuseController"
        ], "匹配预览、执行、下载与严格复用应拆分为独立控制器");

        controllerTypes.Should().NotContain(type => type.Name == "MatchingController",
            "巨型 MatchingController 应被拆分，避免继续堆叠职责");

        foreach (var controllerType in controllerTypes)
        {
            var constructor = controllerType
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .SingleOrDefault();

            constructor.Should().NotBeNull($"{controllerType.Name} 应保留单一公开构造函数，便于依赖管理");
            constructor!.GetParameters().Length.Should().BeLessThanOrEqualTo(3,
                $"{controllerType.Name} 应保持轻量依赖，避免再次退化为巨型控制器");
        }
    }

    [Fact]
    public void MatchingExecutionController_LlmStream_ShouldDeclareAuditOperationAttribute()
    {
        var method = typeof(MatchingExecutionController).GetMethod(nameof(MatchingExecutionController.LlmStream));

        method.Should().NotBeNull();
        var attribute = method!
            .GetCustomAttributes(typeof(AuditOperationAttribute), inherit: true)
            .OfType<AuditOperationAttribute>()
            .SingleOrDefault();

        attribute.Should().NotBeNull("SSE 流式填充入口也应进入控制器审计链路");
        attribute!.Operation.Should().Be("llm-stream");
        attribute.Resource.Should().Be("matching-fill");
    }

    [Fact]
    public void MatchingAndFileCompareControllers_ShouldDeclareAuthorizeAttributes()
    {
        typeof(MatchingApiControllerBase)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Should()
            .NotBeEmpty("匹配相关控制器应显式声明鉴权，避免仅依赖全局兜底策略");

        typeof(FileCompareController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Should()
            .NotBeEmpty("文件对比控制器应显式声明鉴权，避免后续调整中间件时意外裸露");
    }

    [Fact]
    public void MatchingFillTask_ShouldContainOwnershipMetadata()
    {
        var properties = typeof(AcceptanceSpecSystem.Data.Entities.MatchingFillTask)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

        properties.Should().Contain("CreatedByUserId", "匹配任务需要记录创建用户，才能校验下载/复用归属");
        properties.Should().Contain("CompanyId", "匹配任务需要记录公司上下文，避免跨组织任务穿透");
    }

    [Fact]
    public void MatchingWorkflowService_ShouldCarryOwnershipAndPayloadVersion()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("PayloadVersion", "任务快照应带版本元数据，便于未来兼容迁移");
        content.Should().Contain("DownloadAsync(ClaimsPrincipal user, string taskId)", "下载接口应结合当前用户校验任务归属");
        content.Should().Contain("PreviewStrictReuseAsync(ClaimsPrincipal user, StrictReusePreviewRequest request)", "严格复用预检也应校验任务归属");
        content.Should().Contain("ExecuteStrictReuseAsync(ClaimsPrincipal user, StrictReuseExecuteRequest request)", "严格复用执行也应校验任务归属");
    }

    [Fact]
    public void PromptTemplateRepository_SetDefault_ShouldUseTransaction()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Data/Repositories/PromptTemplateRepository.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("BeginTransactionAsync", "设置默认模板应使用事务收拢并发窗口");
        content.Should().Contain("ExecuteUpdateAsync", "设置默认模板应以集合更新清理旧默认，避免逐条切换带来竞态");
    }

    [Fact]
    public void AiServiceSelection_ShouldDependOnInterface()
    {
        var files = new[]
        {
            "src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs",
            "src/AcceptanceSpecSystem.Api/Services/SpecSemanticSearchService.cs",
            "src/AcceptanceSpecSystem.Core/Matching/Services/LlmMatchingAssistService.cs",
            "src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelEmbeddingService.cs"
        };

        foreach (var relativePath in files)
        {
            var content = File.ReadAllText(Path.Combine(GetRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
            content.Should().NotContain("private readonly AiServiceSelector", $"{relativePath} 不应再以字段形式依赖具体类");
            content.Should().NotContain("(AiServiceSelector ", $"{relativePath} 不应再以构造函数参数形式依赖具体类");
            content.Should().Contain("IAiServiceSelector", $"{relativePath} 应显式注入 IAiServiceSelector");
        }
    }

    [Fact]
    public void SemanticKernelFactory_ShouldAvoidHardcodedAzurePreviewVersion_AndSyncBlockingDispose()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Core/AI/SemanticKernel/SemanticKernelServiceFactory.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("\"2024-02-15-preview\"", "Azure OpenAI API 版本应配置化，而不是硬编码在代码里");
        content.Should().NotContain("GetAwaiter().GetResult()", "异步资源释放不应通过同步阻塞完成");
    }

    [Fact]
    public void OllamaNativeChatCompletionService_ShouldNotMutateInjectedHttpClientTimeout()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Core/AI/SemanticKernel/OllamaNativeChatCompletionService.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("_httpClient.Timeout =", "Ollama 原生服务不应改写注入 HttpClient 的共享状态");
    }

    [Fact]
    public void AuthRolePermissionTouch_ShouldUseSetBasedUpdate()
    {
        var authRolesContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Controllers/AuthRolesController.cs".Replace('/', Path.DirectorySeparatorChar)));
        authRolesContent.Should().Contain("ExecuteUpdateAsync", "角色变更触达用户权限版本应使用集合更新，而不是先拉全量用户到内存");

        var seedContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Services/AuthUserSeedService.cs".Replace('/', Path.DirectorySeparatorChar)));
        seedContent.Should().Contain("ExecuteUpdateAsync", "初始化角色修正权限版本时也应使用集合更新");
        seedContent.Should().Contain("BeginTransactionAsync", "根组织路径初始化应通过事务保证原子性");
    }

    [Fact]
    public void LoginAndSmartFillViews_ShouldContainReviewFixes()
    {
        var loginContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/login/index.vue".Replace('/', Path.DirectorySeparatorChar)));
        loginContent.Should().Contain("const topMenu = getTopMenu(true);", "登录页应先保存菜单对象，再校验是否可跳转");
        loginContent.Should().Contain("if (!topMenu?.path)", "登录页应在无菜单时给出明确提示，而不是抛空引用");

        var smartFillContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/index.vue".Replace('/', Path.DirectorySeparatorChar)));
        smartFillContent.Should().Contain("previewAbortController", "批量预览应保留 AbortController 取消陈旧请求");
        smartFillContent.Should().Contain("signal: controller.signal", "批量预览请求应显式传递取消信号");
        smartFillContent.Should().Contain("document.body.appendChild(a);", "Object URL 下载前应把锚点挂到 DOM，兼容 Firefox/Safari");
    }

    [Fact]
    public void SessionExpiryHandling_ShouldPreserveRedirectPath_AndRequireExplicitRelogin()
    {
        var httpContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/utils/http/index.ts".Replace('/', Path.DirectorySeparatorChar)));
        httpContent.Should().Contain("ElMessageBox.alert", "登录态过期时应以确认弹框提示用户，而不是只闪过一条消息后立即跳转");
        httpContent.Should().Contain("useUserStoreHook().logOut(currentPath)", "会话失效后应携带当前页面地址，便于重新登录后回跳");

        var userStoreContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/store/modules/user.ts".Replace('/', Path.DirectorySeparatorChar)));
        userStoreContent.Should().Contain("logOut(redirectPath?: string)", "登出逻辑应支持接收回跳地址");
        userStoreContent.Should().Contain("query: { redirect: redirectPath }", "跳登录页时应保留会话失效前的页面地址");

        var loginContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/login/index.vue".Replace('/', Path.DirectorySeparatorChar)));
        loginContent.Should().Contain("useRoute", "登录页应读取 redirect 查询参数，支持重新登录后回跳");
        loginContent.Should().Contain("route.query.redirect", "登录成功后应优先跳回会话失效前的页面");
    }

    [Fact]
    public void Program_ShouldNotFallbackCorsToAllowAnyOrigin()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Program.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("AllowAnyOrigin()", "CORS 来源缺失时不应退化为全开放");
        content.Should().Contain("Cors:AllowedOrigins", "启动期应对 CORS 来源配置做显式校验");
    }

    [Fact]
    public void ProductionConfig_ShouldUseExplicitCorsOrigins()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/appsettings.Production.json".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("\"AllowedOrigins\": [ \"*\" ]",
            "Production 配置必须给出显式 CORS 白名单，不能与启动期校验相冲突");
    }

    [Fact]
    public void WebBuildScript_ShouldRunTypecheckBeforeBundling()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/package.json".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("\"build\": \"pnpm typecheck &&",
            "前端 build 应先执行 typecheck，避免类型错误被 vite 构建掩盖");
    }

    [Fact]
    public void WordFile_ShouldContainOwnershipMetadata_AndDocumentsController_ShouldApplyWordFileScope()
    {
        var propertyNames = typeof(AcceptanceSpecSystem.Data.Entities.WordFile)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

        propertyNames.Should().Contain("CompanyId");
        propertyNames.Should().Contain("CreatedByUserId");
        propertyNames.Should().Contain("OwnerOrgUnitId");

        var documentsContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs".Replace('/', Path.DirectorySeparatorChar)));
        documentsContent.Should().Contain("WordFileDataScopeHelper",
            "文档控制器应对文件列表与单文件访问执行文件级范围校验");
        documentsContent.Should().Contain("GetAccessibleWordFileAsync",
            "文档控制器应统一通过归属校验读取文件，避免各接口漏校验");
    }

    [Fact]
    public void SmartFillViews_ShouldUseMatchingFillPermissionNames_AndPreserveManualConfirmation()
    {
        var smartFillContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/index.vue".Replace('/', Path.DirectorySeparatorChar)));
        smartFillContent.Should().Contain("btn:matching-fill:llm-stream",
            "LLM 流式复核按钮权限应与后端 matching-fill 资源保持一致");
        smartFillContent.Should().NotContain("btn:matching:llm-stream",
            "旧的 matching:llm-stream 权限命名应被移除");

        var tabsContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/BatchPreviewTabs.vue".Replace('/', Path.DirectorySeparatorChar)));
        tabsContent.Should().Contain("manualConfirmed",
            "批量预览页应把人工确认标记透传到执行请求，避免类型漂移和行为回退");
    }

    [Fact]
    public void PromptTemplateView_ShouldUseGranularPreviewAndResetPermissions()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/config/prompt-templates/index.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("btn:prompt-template:preview",
            "模板预览应使用独立的 preview 权限");
        content.Should().Contain("btn:prompt-template:reset-system",
            "恢复默认应使用独立的 reset-system 权限");
    }

    [Fact]
    public void LegacyConfigRedirects_ShouldNotHangUnderConfigMenuRoute()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/router/modules/config.ts".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("AuthRolesConfigLegacy",
            "旧 /config/* 兼容跳转不应继续挂在 Config 父路由下，否则会先被 menu:config 拦住");
        content.Should().NotContain("SystemUsersConfigLegacy",
            "旧 /config/* 兼容跳转不应继续挂在 Config 父路由下，否则会先被 menu:config 拦住");
        content.Should().NotContain("OrgUnitsConfigLegacy",
            "旧 /config/* 兼容跳转不应继续挂在 Config 父路由下，否则会先被 menu:config 拦住");
    }

    [Fact]
    public void HttpRequestInterceptor_ShouldPreserveAuditHeaders_WhenBeforeRequestCallbackExists()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/utils/http/index.ts".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("ensureAuditHeaders(config);", "请求进入自定义 beforeRequestCallback 分支前后都应补齐审计头");

        var callbackIndex = content.IndexOf("if (typeof config.beforeRequestCallback === \"function\")", StringComparison.Ordinal);
        var headerIndex = content.IndexOf("ensureAuditHeaders(config);", StringComparison.Ordinal);
        headerIndex.Should().BeGreaterThanOrEqualTo(0);
        callbackIndex.Should().BeGreaterThan(0);
        headerIndex.Should().BeLessThan(callbackIndex, "应先补齐审计头，再交给 beforeRequestCallback 自定义处理");
    }

    [Fact]
    public void EmbeddingCacheRepository_DeleteMethods_ShouldUseExecuteDeleteAsync()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Data/Repositories/EmbeddingCacheRepository.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("Where(e => e.ModelName == modelName)\n            .ExecuteDeleteAsync()", "按模型名批量删除应直接下推到数据库");
        content.Should().Contain("Where(e => e.ExpiresAt != null && e.ExpiresAt < beforeTime)\n            .ExecuteDeleteAsync()", "过期缓存清理应直接下推到数据库");
        content.Should().Contain("Where(e => e.ModelName == modelName && e.ModelVersion != modelVersion)\n            .ExecuteDeleteAsync()", "按模型版本批量失效应直接下推到数据库");
    }

    [Fact]
    public void PromptTemplateProvider_ShouldAvoidDirectAppDbContextDependency()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Data/Providers/CoreProviderAdapters.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("private readonly AppDbContext", "PromptTemplateProvider 不应再直接依赖具体 DbContext");
        content.Should().Contain("IUnitOfWork", "PromptTemplateProvider 至少应通过 UoW 抽象提交变更");
    }

    [Fact]
    public void AuthAccessService_ShouldAvoidDirectAppDbContextDependency()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Services/AuthAccessService.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("private readonly AppDbContext", "鉴权访问服务不应再直接持有 AppDbContext");
        content.Should().Contain("ISystemUserRepository", "用户访问应通过专用仓储抽象完成");
    }

    [Fact]
    public void AuthDataScopeService_ShouldUseMemoryCache()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Services/AuthDataScopeService.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("IMemoryCache", "数据范围服务应通过内存缓存复用组织树计算结果");
        content.Should().Contain("_memoryCache", "数据范围服务应持有缓存实例");
    }

    [Fact]
    public void LegacyTextProcessingRepositories_ShouldBeRemoved()
    {
        var repositoryRoot = GetRepositoryRoot();
        var legacyFiles = new[]
        {
            "src/AcceptanceSpecSystem.Data/Repositories/KeywordRepository.cs",
            "src/AcceptanceSpecSystem.Data/Repositories/IKeywordRepository.cs",
            "src/AcceptanceSpecSystem.Data/Repositories/TextProcessingConfigRepository.cs",
            "src/AcceptanceSpecSystem.Data/Repositories/ITextProcessingConfigRepository.cs",
            "src/AcceptanceSpecSystem.Data/Repositories/SynonymRepository.cs",
            "src/AcceptanceSpecSystem.Data/Repositories/ISynonymRepository.cs"
        };

        foreach (var relativePath in legacyFiles)
        {
            File.Exists(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .Should().BeFalse($"{relativePath} 应随旧文本预处理体系一起移除");
        }
    }

    [Fact]
    public void MatchingWorkflowService_ShouldRejectTasksWithoutOwnershipMetadata()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("!entity.CreatedByUserId.HasValue", "缺少用户归属的任务应被显式拒绝");
        content.Should().Contain("!entity.CompanyId.HasValue", "缺少公司归属的任务应被显式拒绝");
    }

    [Fact]
    public void AppDbContext_ShouldLogWhenApiKeyDecryptFallbackIsTriggered()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("Trace.TraceWarning", "ApiKey 解密兼容路径至少应输出告警，避免静默吞错");
    }

    [Fact]
    public void MatchingApiControllerBase_ShouldDocumentExceptionBoundary()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Controllers/MatchingApiControllerBase.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("其余异常继续交给全局异常中间件处理", "基类应明确说明异常边界，避免控制器和中间件职责含混");
    }

    [Fact]
    public void MatchingTaskController_Download_ShouldConstrainTaskIdFormat()
    {
        var method = typeof(MatchingTaskController).GetMethod(nameof(MatchingTaskController.Download));

        method.Should().NotBeNull();
        var attribute = method!
            .GetCustomAttributes(typeof(HttpGetAttribute), inherit: true)
            .OfType<HttpGetAttribute>()
            .SingleOrDefault();

        attribute.Should().NotBeNull("任务下载接口应限制 taskId 路由格式");
        attribute!.Template.Should().Contain("{taskId:regex(^[[a-f0-9]]{{32}}$)}");
    }

    [Fact]
    public void SemanticKernelFactory_ShouldAvoidNullForgivingForRequiredModels()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Core/AI/SemanticKernel/SemanticKernelServiceFactory.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("config.LlmModel!", "必填模型应通过显式 Guard 获取，而不是 null-forgiving");
        content.Should().NotContain("config.EmbeddingModel!", "必填模型应通过显式 Guard 获取，而不是 null-forgiving");
    }

    [Fact]
    public void StrictReuseDialog_ShouldWarnWhenPermissionPropsAreMissing()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/StrictReuseDialog.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("console.warn", "开发环境下应提示父组件遗漏权限 props");
        content.Should().Contain("import.meta.env.DEV", "仅开发环境输出权限 props 警告即可");
    }

    [Fact]
    public void UserStore_LogOut_ShouldClearPermissionAndRouteCaches()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/store/modules/user.ts".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("usePermissionStoreHook().clearAllCachePage()", "登出时应清掉权限菜单和 keepAlive 缓存");
        content.Should().Contain("storageLocal().removeItem(\"async-routes\")", "登出时应清理动态路由缓存");
    }

    [Fact]
    public void SmartFill_OnUnmount_ShouldAbortPreviewRequestsToo()
    {
        var smartFillContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/index.vue".Replace('/', Path.DirectorySeparatorChar)));
        smartFillContent.Should().Contain(
            "onBeforeUnmount(() => {\n  invalidatePendingPreview();\n  stopLlmStream();\n});",
            "页面卸载时应同时取消未完成的批量预览请求和流式请求，避免离页后仍占用后端算力");
    }

    private static string[] ReadFile(string relativePath)
    {
        var repositoryRoot = GetRepositoryRoot();
        return File.ReadAllLines(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
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
