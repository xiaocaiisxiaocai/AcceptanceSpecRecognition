using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.Matching.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AcceptanceSpecSystem.Core.Tests;

public class LlmReviewPromptTests
{
    [Fact]
    public async Task ReviewAsync_WhenImportDuplicateScene_ShouldUseDedicatedTemplateAndInjectWorkflowScene()
    {
        var promptProvider = new RecordingPromptTemplateProvider(
            "【业务场景】{{ workflowScene }}\n源项目：{{ sourceProject }}\n仅返回严格 JSON：\n{\"score\":88,\"reason\":\"复核通过\",\"commentary\":\"已比较\"}");
        var selector = new StubAiServiceSelector();
        var chatService = new RecordingChatCompletionService();
        var service = new LlmMatchingAssistService(
            promptProvider,
            selector,
            new StubSemanticKernelServiceFactory(chatService),
            NullLogger<LlmMatchingAssistService>.Instance);

        var result = await service.ReviewAsync(new LlmReviewRequest
        {
            SourceProject = "导入项目",
            SourceSpecification = "导入规格",
            BestMatchProject = "历史项目",
            BestMatchSpecification = "历史规格",
            BaseScore = 91.5,
            ReviewScene = LlmReviewScene.ImportDuplicateReview
        });

        result.Should().NotBeNull();
        promptProvider.LastScene.Should().Be(PromptTemplateScene.ImportDuplicateReview);
        chatService.LastPrompt.Should().Contain("【业务场景】导入重复复核");
        chatService.LastPrompt.Should().Contain("源项目：导入项目");
    }

    [Fact]
    public void MatchingEquivalencePromptTemplate_ShouldDescribeUnifiedReviewGate()
    {
        var definition = PromptTemplateCatalog
            .GetSystemTemplates()
            .Single(template => template.Scene == PromptTemplateScene.MatchingEquivalenceAdjudication);

        definition.UsageDescription.Should().NotContain("边界样本");
        definition.UsageDescription.Should().Contain("最佳候选");
    }

    [Fact]
    public void MatchingEquivalencePromptTemplate_ShouldIncludeFewShotExamples_AndPreservePreviousDefaultAsLegacy()
    {
        var definition = PromptTemplateCatalog
            .GetSystemTemplates()
            .Single(template => template.Scene == PromptTemplateScene.MatchingEquivalenceAdjudication);

        // 新默认内容含 few-shot 示例段，且含"整句同义复述"长文本改写引导
        definition.DefaultContent.Should().Contain("【判定示例】");
        definition.DefaultContent.Should().Contain("整句同义复述");

        // 升级链保留历次旧默认，且旧内容都不含"整句同义复述"段
        // （V4 已含 few-shot 但缺整句同义复述引导，故不再以 few-shot 作为新旧分界）
        definition.AdditionalLegacyContents.Should().NotBeNull();
        definition.AdditionalLegacyContents!.Should().HaveCountGreaterThanOrEqualTo(4);
        definition.AdditionalLegacyContents!.Should().OnlyContain(content => !content.Contains("整句同义复述"));
    }

    [Fact]
    public async Task ReviewAsync_WhenUserContentContainsPlaceholderToken_ShouldPreserveLiteralTextInPrompt()
    {
        var promptProvider = new RecordingPromptTemplateProvider(
            "源项目：{{sourceProject}}\n当前决策：{{currentDecision}}\n仅返回严格 JSON：\n{\"score\":88,\"reason\":\"复核通过\",\"commentary\":\"已比较\"}");
        var selector = new StubAiServiceSelector();
        var chatService = new RecordingChatCompletionService();
        var service = new LlmMatchingAssistService(
            promptProvider,
            selector,
            new StubSemanticKernelServiceFactory(chatService),
            NullLogger<LlmMatchingAssistService>.Instance);

        var result = await service.ReviewAsync(new LlmReviewRequest
        {
            SourceProject = "导入{{currentDecision}}",
            SourceSpecification = "导入规格",
            BestMatchProject = "历史项目",
            BestMatchSpecification = "历史规格",
            CurrentDecision = "manualReview"
        });

        result.Should().NotBeNull();
        chatService.LastPrompt.Should().Contain("源项目：导入{{currentDecision}}");
        chatService.LastPrompt.Should().Contain("当前决策：manualReview");
        chatService.LastPrompt.Should().NotContain("源项目：导入manualReview");
    }

    [Fact]
    public async Task AdjudicateStructureAsync_ShouldUseDedicatedSmartConfigTemplate()
    {
        var promptProvider = new RecordingPromptTemplateProvider(
            "【文档表格摘要 JSON】{{documentTablesJson}}\n【规则识别结果 JSON】{{ruleCandidatesJson}}\n仅返回严格 JSON：\n{\"tables\":[{\"tableIndex\":0,\"specificationColumnIndex\":1,\"confidence\":0.86}],\"confidence\":0.86,\"decision\":\"needConfirm\",\"reason\":\"已补规格列\"}");
        var selector = new StubAiServiceSelector();
        var chatService = new RecordingChatCompletionService(
            "{\"tables\":[{\"tableIndex\":0,\"specificationColumnIndex\":1,\"confidence\":0.86}],\"confidence\":0.86,\"decision\":\"needConfirm\",\"reason\":\"已补规格列\"}");
        var service = new LlmMatchingAssistService(
            promptProvider,
            selector,
            new StubSemanticKernelServiceFactory(chatService),
            NullLogger<LlmMatchingAssistService>.Instance);

        var result = await service.AdjudicateAsync(new LlmDocumentStructureAdjudicationRequest
        {
            DocumentTablesJson = "[{\"tableIndex\":0}]",
            RuleCandidates =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = 0,
                    ProjectColumnIndex = 0,
                    SpecificationColumnIndex = null,
                    Confidence = 0.7,
                    Source = DocumentStructureCandidateSource.Rule
                }
            ]
        });

        result.Should().NotBeNull();
        result!.Tables.Should().ContainSingle();
        result.Tables[0].SpecificationColumnIndex.Should().Be(1);
        promptProvider.LastScene.Should().Be(PromptTemplateScene.SmartConfigStructureRecognition);
        chatService.LastPrompt.Should().Contain("【文档表格摘要 JSON】[{\"tableIndex\":0}]");
        chatService.LastPrompt.Should().Contain("\"ProjectColumnIndex\":0");
    }

    [Fact]
    public async Task AdjudicateStructureAsync_ShouldRenderReferenceCasesIntoPrompt()
    {
        var promptProvider = new RecordingPromptTemplateProvider(
            "【历史结构案例 JSON】{{referenceCasesJson}}\n【文档表格摘要 JSON】{{documentTablesJson}}\n【规则识别结果 JSON】{{ruleCandidatesJson}}\n仅返回严格 JSON：\n{\"tables\":[{\"tableIndex\":0,\"specificationColumnIndex\":1,\"confidence\":0.86}],\"confidence\":0.86,\"decision\":\"needConfirm\",\"reason\":\"参考历史案例\"}");
        var selector = new StubAiServiceSelector();
        var chatService = new RecordingChatCompletionService(
            "{\"tables\":[{\"tableIndex\":0,\"specificationColumnIndex\":1,\"confidence\":0.86}],\"confidence\":0.86,\"decision\":\"needConfirm\",\"reason\":\"参考历史案例\"}");
        var service = new LlmMatchingAssistService(
            promptProvider,
            selector,
            new StubSemanticKernelServiceFactory(chatService),
            NullLogger<LlmMatchingAssistService>.Instance);

        await service.AdjudicateAsync(new LlmDocumentStructureAdjudicationRequest
        {
            DocumentTablesJson = "[{\"tableIndex\":0}]",
            RuleCandidates =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = 0,
                    ProjectColumnIndex = 0,
                    Confidence = 0.7,
                    Source = DocumentStructureCandidateSource.Rule
                }
            ],
            ReferenceCases =
            [
                new DocumentStructureReferenceCase
                {
                    TemplateName = "历史模板",
                    Headers = ["检查对象", "管制条件", "供应商回复", "补充说明"],
                    UsageCount = 7,
                    Similarity = 0.92,
                    Mapping = new DocumentStructureCandidate
                    {
                        TableIndex = 0,
                        ProjectColumnIndex = 0,
                        SpecificationColumnIndex = 1,
                        AcceptanceColumnIndex = 2,
                        RemarkColumnIndex = 3,
                        HeaderRowIndex = 0,
                        HeaderRowCount = 1,
                        DataStartRowIndex = 1,
                        IsSpecificationOnly = false,
                        Confidence = 1,
                        Source = DocumentStructureCandidateSource.Template
                    }
                }
            ]
        });

        chatService.LastPrompt.Should().Contain("【历史结构案例 JSON】");
        chatService.LastPrompt.Should().Contain("历史模板");
        chatService.LastPrompt.Should().Contain("检查对象");
        chatService.LastPrompt.Should().Contain("\"SpecificationColumnIndex\":1");
        chatService.LastPrompt.Should().NotContain("{{referenceCasesJson}}");
    }

    private sealed class RecordingPromptTemplateProvider : IPromptTemplateProvider
    {
        private readonly string _content;

        public RecordingPromptTemplateProvider(string content)
        {
            _content = content;
        }

        public PromptTemplateScene? LastScene { get; private set; }

        public Task<PromptTemplateModel> GetOrCreateSystemAsync(
            PromptTemplateScene scene,
            string name,
            string displayName,
            string defaultContent,
            CancellationToken cancellationToken = default)
        {
            LastScene = scene;
            return Task.FromResult(new PromptTemplateModel
            {
                Id = 1,
                Content = _content
            });
        }

        public Task SaveContentAsync(int id, string content, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubAiServiceSelector : IAiServiceSelector
    {
        public Task<IReadOnlyList<AiServiceConfigModel>> GetCandidatesAsync(
            AiServicePurpose purpose,
            int? preferredId = null,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AiServiceConfigModel> configs =
            [
                new AiServiceConfigModel
                {
                    Id = 1,
                    Name = "test-llm",
                    Purpose = AiServicePurpose.Llm,
                    LlmModel = "gpt-test"
                }
            ];

            return Task.FromResult(configs);
        }
    }

    private sealed class StubSemanticKernelServiceFactory : ISemanticKernelServiceFactory
    {
        private readonly IChatCompletionService _chatService;

        public StubSemanticKernelServiceFactory(IChatCompletionService chatService)
        {
            _chatService = chatService;
        }

        public IChatCompletionService CreateChatCompletionService(AiServiceConfigModel config) => _chatService;

        public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiServiceConfigModel config)
            => throw new NotSupportedException();
    }

    private sealed class RecordingChatCompletionService : IChatCompletionService
    {
        private readonly string _response;

        public RecordingChatCompletionService(string? response = null)
        {
            _response = response ?? "{\"score\":88,\"reason\":\"复核通过\",\"commentary\":\"已比较\"}";
        }

        public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

        public string LastPrompt { get; private set; } = string.Empty;

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            LastPrompt = chatHistory.Last().Content ?? string.Empty;
            IReadOnlyList<ChatMessageContent> result =
            [
                new ChatMessageContent(AuthorRole.Assistant, _response)
            ];
            return Task.FromResult(result);
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastPrompt = chatHistory.Last().Content ?? string.Empty;
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, _response);
            await Task.CompletedTask;
        }
    }
}
