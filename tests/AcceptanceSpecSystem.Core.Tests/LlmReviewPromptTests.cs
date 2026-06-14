using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
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

        // 新默认内容含 few-shot 示例段
        definition.DefaultContent.Should().Contain("【判定示例】");

        // 升级链保留历次旧默认（含本次之前的默认），且旧内容都不含 few-shot 段
        definition.AdditionalLegacyContents.Should().NotBeNull();
        definition.AdditionalLegacyContents!.Should().HaveCountGreaterThanOrEqualTo(3);
        definition.AdditionalLegacyContents!.Should().OnlyContain(content => !content.Contains("【判定示例】"));
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
                new ChatMessageContent(AuthorRole.Assistant, "{\"score\":88,\"reason\":\"复核通过\",\"commentary\":\"已比较\"}")
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
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, "{\"score\":88,\"reason\":\"复核通过\",\"commentary\":\"已比较\"}");
            await Task.CompletedTask;
        }
    }
}
