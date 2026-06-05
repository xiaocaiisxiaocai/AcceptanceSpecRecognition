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
using System.Collections.Concurrent;
using System.Reflection;

namespace AcceptanceSpecSystem.Core.Tests;

public class LlmMatchingAssistFallbackTests
{
    private const string ApprovedSourceProject = "PanelLine";
    private const string ApprovedSourceSpecification = "AGV Dock-Bay";
    private const string ApprovedBestProject = "PanelLine";
    private const string ApprovedBestSpecification = "AGV Dock Bay";

    [Fact]
    public async Task AdjudicateAsync_WhenFirstServiceReturnsSchemaCompatibleButSemanticallyInvalidPayload_ShouldFallbackToNextService()
    {
        var promptProvider = new StaticPromptTemplateProvider(
            """
            源项目：{{sourceProject}}
            源规格：{{sourceSpecification}}
            候选项目：{{candidateProject}}
            候选规格：{{candidateSpecification}}
            当前决策：{{currentDecision}}
            得分明细：{{scoreDetailsJson}}
            证据摘要：{{evidenceSummaryJson}}
            冲突摘要：{{conflictSummaryJson}}
            """);
        var firstChat = new FixedChatCompletionService(
            """{"verdict":"equivalent","reasonType":"semantic_difference","reason":"格式上等价","confidence":0.82}""");
        var secondChat = new FixedChatCompletionService(
            """
            下面是裁决结果：
            ```json
            {"verdict":"different","reasonType":"symbol_conflict","reason":"22V 与 220V 存在关键电压冲突","confidence":0.97}
            ```
            """);
        var selector = new FixedAiServiceSelector(CreateConfig(11, "llm-a"), CreateConfig(12, "llm-b"));
        var factory = new RoutingSemanticKernelServiceFactory(new Dictionary<int, IChatCompletionService>
        {
            [11] = firstChat,
            [12] = secondChat
        });
        var service = new LlmMatchingAssistService(
            promptProvider,
            selector,
            factory,
            NullLogger<LlmMatchingAssistService>.Instance);

        var result = await service.AdjudicateAsync(new LlmEquivalenceAdjudicationRequest
        {
            SourceProject = "水/电/气",
            SourceSpecification = "电力规格要求: 380V三相/50HZ或22V/50HZ",
            CandidateProject = "水/电/气",
            CandidateSpecification = "电力规格要求: 380V三相/50HZ或220V/50HZ",
            CurrentDecision = "manualReview",
            ScoreDetails = new Dictionary<string, double>
            {
                ["Embedding"] = 0.99,
                ["Numeric"] = 0.15
            },
            EvidenceSummary = ["voltage evidence matched"],
            ConflictSummary = ["numeric conflict: 22V vs 220V"]
        });

        result.Should().NotBeNull();
        result!.Verdict.Should().Be(LlmEquivalenceVerdict.Different);
        result.ReasonType.Should().Be(LlmEquivalenceReasonType.SymbolConflict);
        result.Confidence.Should().BeApproximately(0.97, 0.0001);
        result.Reason.Should().Contain("22V");
        firstChat.CallCount.Should().Be(1);
        secondChat.CallCount.Should().Be(1);
        secondChat.LastPrompt.Should().Contain("当前决策：manualReview");
        secondChat.LastPrompt.Should().Contain("\"Embedding\":0.99");
        secondChat.LastPrompt.Should().Contain("\"Numeric\":0.15");
        secondChat.LastPrompt.Should().Contain("voltage evidence matched");
        secondChat.LastPrompt.Should().Contain("22V vs 220V");
    }

    [Fact]
    public async Task ReviewAsync_WhenFirstServiceReturnsMalformedPayload_ShouldFallbackToNextService()
    {
        var promptProvider = new StaticPromptTemplateProvider(
            "【业务场景】{{ workflowScene }}\n源项目：{{ sourceProject }}\n当前决策：{{ currentDecision }}\n仅返回严格 JSON");
        var firstChat = new FixedChatCompletionService("""{"score":"N/A","reason":"无法判断","commentary":"缺少结构"}""");
        var secondChat = new FixedChatCompletionService(
            """
            <think>复核项目与规格</think>
            {"score":88,"reason":"复核通过","commentary":"已比较项目与规格"}
            """);
        var selector = new FixedAiServiceSelector(CreateConfig(21, "llm-a"), CreateConfig(22, "llm-b"));
        var factory = new RoutingSemanticKernelServiceFactory(new Dictionary<int, IChatCompletionService>
        {
            [21] = firstChat,
            [22] = secondChat
        });
        var service = new LlmMatchingAssistService(
            promptProvider,
            selector,
            factory,
            NullLogger<LlmMatchingAssistService>.Instance);

        var result = await service.ReviewAsync(new LlmReviewRequest
        {
            SourceProject = "导入项目",
            SourceSpecification = "导入规格",
            BestMatchProject = "历史项目",
            BestMatchSpecification = "历史规格",
            CurrentDecision = "manualReview",
            ReviewScene = LlmReviewScene.ImportDuplicateReview
        });

        result.Should().NotBeNull();
        result!.Score.Should().Be(88);
        result.Reason.Should().Be("复核通过");
        result.Commentary.Should().Be("已比较项目与规格");
        firstChat.CallCount.Should().Be(1);
        secondChat.CallCount.Should().Be(1);
        secondChat.LastPrompt.Should().Contain("【业务场景】导入重复复核");
        secondChat.LastPrompt.Should().Contain("源项目：导入项目");
        secondChat.LastPrompt.Should().Contain("当前决策：manualReview");
    }

    [Fact]
    public async Task ReviewAsync_WhenExplicitServiceReturnsMalformedPayload_ShouldNotFallbackToNextService()
    {
        var promptProvider = new StaticPromptTemplateProvider(
            "【业务场景】{{ workflowScene }}\n源项目：{{ sourceProject }}\n当前决策：{{ currentDecision }}\n仅返回严格 JSON");
        var firstChat = new FixedChatCompletionService("""{"score":"N/A","reason":"无法判断","commentary":"缺少结构"}""");
        var secondChat = new FixedChatCompletionService(
            """{"score":88,"reason":"后备服务返回有效结果","commentary":"不应被调用"}""");
        var selector = new FixedAiServiceSelector(CreateConfig(21, "llm-a"), CreateConfig(22, "llm-b"));
        var factory = new RoutingSemanticKernelServiceFactory(new Dictionary<int, IChatCompletionService>
        {
            [21] = firstChat,
            [22] = secondChat
        });
        var service = new LlmMatchingAssistService(
            promptProvider,
            selector,
            factory,
            NullLogger<LlmMatchingAssistService>.Instance);

        var result = await service.ReviewAsync(new LlmReviewRequest
        {
            SourceProject = "导入项目",
            SourceSpecification = "导入规格",
            BestMatchProject = "历史项目",
            BestMatchSpecification = "历史规格",
            CurrentDecision = "manualReview",
            ReviewScene = LlmReviewScene.ImportDuplicateReview,
            LlmServiceId = 21
        });

        result.Should().BeNull();
        firstChat.CallCount.Should().Be(1);
        secondChat.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ReviewStreamAsync_WhenFirstServiceCompletesWithEmptyStream_ShouldFallbackToNextService()
    {
        var promptProvider = new StaticPromptTemplateProvider(
            "源项目：{{ sourceProject }}\n源规格：{{ sourceSpecification }}\n仅返回严格 JSON");
        var firstChat = new FixedChatCompletionService(string.Empty);
        var secondChat = new FixedChatCompletionService(
            """
            {"score":92,"reason":"后备服务返回有效结果","commentary":"空流后已切换后备服务"}
            """);
        var selector = new FixedAiServiceSelector(CreateConfig(41, "llm-a"), CreateConfig(42, "llm-b"));
        var factory = new RoutingSemanticKernelServiceFactory(new Dictionary<int, IChatCompletionService>
        {
            [41] = firstChat,
            [42] = secondChat
        });
        var service = new LlmMatchingAssistService(
            promptProvider,
            selector,
            factory,
            NullLogger<LlmMatchingAssistService>.Instance);

        var chunks = new List<string>();
        await foreach (var chunk in service.ReviewStreamAsync(new LlmReviewRequest
                       {
                           SourceProject = ApprovedSourceProject,
                           SourceSpecification = ApprovedSourceSpecification,
                           BestMatchProject = ApprovedBestProject,
                           BestMatchSpecification = ApprovedBestSpecification
                       }))
        {
            chunks.Add(chunk);
        }

        chunks.Should().NotBeEmpty();
        string.Concat(chunks).Should().Contain("\"score\":92");
        firstChat.CallCount.Should().Be(1);
        secondChat.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ReviewStreamAsync_WhenExplicitServiceCompletesWithEmptyStream_ShouldNotFallbackToNextService()
    {
        var promptProvider = new StaticPromptTemplateProvider(
            "源项目：{{ sourceProject }}\n源规格：{{ sourceSpecification }}\n仅返回严格 JSON");
        var firstChat = new FixedChatCompletionService(string.Empty);
        var secondChat = new FixedChatCompletionService(
            """{"score":92,"reason":"后备服务返回有效结果","commentary":"不应被调用"}""");
        var selector = new FixedAiServiceSelector(CreateConfig(41, "llm-a"), CreateConfig(42, "llm-b"));
        var factory = new RoutingSemanticKernelServiceFactory(new Dictionary<int, IChatCompletionService>
        {
            [41] = firstChat,
            [42] = secondChat
        });
        var service = new LlmMatchingAssistService(
            promptProvider,
            selector,
            factory,
            NullLogger<LlmMatchingAssistService>.Instance);

        var act = async () =>
        {
            await foreach (var _ in service.ReviewStreamAsync(new LlmReviewRequest
                           {
                               SourceProject = ApprovedSourceProject,
                               SourceSpecification = ApprovedSourceSpecification,
                               BestMatchProject = ApprovedBestProject,
                               BestMatchSpecification = ApprovedBestSpecification,
                               LlmServiceId = 41
                           }))
            {
            }
        };

        await act.Should().ThrowAsync<AiServiceUnavailableException>();
        firstChat.CallCount.Should().Be(1);
        secondChat.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AdjudicateAsync_WhenOutputContainsExampleJsonAndFinalJson_ShouldParseLastJsonObject()
    {
        var promptProvider = new StaticPromptTemplateProvider(
            "源项目：{{sourceProject}}\n候选项目：{{candidateProject}}\n当前决策：{{currentDecision}}");
        var chat = new FixedChatCompletionService(
            """
            示例 JSON：
            {"verdict":"uncertain","reasonType":"uncertain","reason":"示例，不要采用","confidence":0.1}

            最终结果：
            {"verdict":"different","reasonType":"symbol_conflict","reason":"22V 与 220V 存在关键电压冲突","confidence":1.4}
            """);
        var selector = new FixedAiServiceSelector(CreateConfig(31, "llm-a"));
        var factory = new RoutingSemanticKernelServiceFactory(new Dictionary<int, IChatCompletionService>
        {
            [31] = chat
        });
        var service = new LlmMatchingAssistService(
            promptProvider,
            selector,
            factory,
            NullLogger<LlmMatchingAssistService>.Instance);

        var result = await service.AdjudicateAsync(new LlmEquivalenceAdjudicationRequest
        {
            SourceProject = "水/电/气",
            SourceSpecification = "22V",
            CandidateProject = "水/电/气",
            CandidateSpecification = "220V",
            CurrentDecision = "manualReview"
        });

        result.Should().NotBeNull();
        result!.Verdict.Should().Be(LlmEquivalenceVerdict.Different);
        result.ReasonType.Should().Be(LlmEquivalenceReasonType.SymbolConflict);
        result.Confidence.Should().Be(1.0);
        result.Reason.Should().Contain("22V");
    }

    [Fact]
    public async Task AdjudicateAsync_WhenConcurrentCallsShareSameService_ShouldReusePromptAndAiConfigLookup()
    {
        var promptProvider = new SingleFlightPromptTemplateProvider(
            "源项目：{{sourceProject}}\n源规格：{{sourceSpecification}}\n候选项目：{{candidateProject}}\n候选规格：{{candidateSpecification}}\n当前决策：{{currentDecision}}");
        var selector = new SingleFlightAiServiceSelector(CreateConfig(51, "llm-a"));
        var factory = new RoutingSemanticKernelServiceFactory(new Dictionary<int, IChatCompletionService>
        {
            [51] = new FixedChatCompletionService(
                """{"verdict":"equivalent","reasonType":"equivalent_expression","reason":"项目与规格一致","confidence":1}""")
        });
        var service = new LlmMatchingAssistService(
            promptProvider,
            selector,
            factory,
            NullLogger<LlmMatchingAssistService>.Instance);

        var tasks = Enumerable.Range(1, 6)
            .Select(index => service.AdjudicateAsync(new LlmEquivalenceAdjudicationRequest
            {
                SourceProject = "项目",
                SourceSpecification = $"源规格{index}",
                CandidateProject = "项目",
                CandidateSpecification = $"候选规格{index}",
                CurrentDecision = "manualReview"
            }))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(result => result != null && result.Verdict == LlmEquivalenceVerdict.Equivalent);
        promptProvider.CallCount.Should().Be(1);
        selector.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentLlmCalls_ShouldSerializeDbBackedCacheInitializationAcrossTemplateAndServiceLookup()
    {
        var gate = new NonConcurrentDbGate();
        var promptProvider = new SharedGatePromptTemplateProvider(
            gate,
            "【业务场景】{{workflowScene}}\n源项目：{{sourceProject}}\n系统匹配结果：{{bestMatchProject}}\n当前决策：{{currentDecision}}\n仅返回严格 JSON");
        var selector = new SharedGateAiServiceSelector(gate, CreateConfig(61, "llm-a"));
        var factory = new RoutingSemanticKernelServiceFactory(new Dictionary<int, IChatCompletionService>
        {
            [61] = new FixedChatCompletionService(
                """{"score":92,"reason":"复核通过","commentary":"已比较项目与规格"}""")
        });
        var service = new LlmMatchingAssistService(
            promptProvider,
            selector,
            factory,
            NullLogger<LlmMatchingAssistService>.Instance);

        SeedPromptTemplateCache(
            service,
            PromptTemplateCatalog.GetByScene(PromptTemplateScene.MatchingEquivalenceAdjudication),
            "源项目：{{sourceProject}}\n源规格：{{sourceSpecification}}\n候选项目：{{candidateProject}}\n候选规格：{{candidateSpecification}}\n当前决策：{{currentDecision}}");

        var reviewTask = service.ReviewAsync(new LlmReviewRequest
        {
            SourceProject = "项目",
            SourceSpecification = "源规格",
            BestMatchProject = "项目",
            BestMatchSpecification = "候选规格",
            CurrentDecision = "manualReview"
        });

        await gate.WaitUntilEnteredAsync();

        var adjudicationTask = service.AdjudicateAsync(new LlmEquivalenceAdjudicationRequest
        {
            SourceProject = "项目",
            SourceSpecification = "源规格",
            CandidateProject = "项目",
            CandidateSpecification = "候选规格",
            CurrentDecision = "manualReview"
        });

        var act = async () => await Task.WhenAll(reviewTask, adjudicationTask);

        await act.Should().NotThrowAsync();
        promptProvider.CallCount.Should().Be(1);
        selector.CallCount.Should().Be(1);
    }

    private static AiServiceConfigModel CreateConfig(int id, string name)
    {
        return new AiServiceConfigModel
        {
            Id = id,
            Name = name,
            Purpose = AiServicePurpose.Llm,
            LlmModel = "gpt-test"
        };
    }

    private static void SeedPromptTemplateCache(
        LlmMatchingAssistService service,
        SystemPromptTemplateDefinition definition,
        string content)
    {
        var field = typeof(LlmMatchingAssistService)
            .GetField("_promptTemplateCache", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();

        var cache = (ConcurrentDictionary<string, PromptTemplateModel>)field!.GetValue(service)!;
        cache[$"{(int)definition.Scene}:{definition.Name}"] = new PromptTemplateModel
        {
            Id = (int)definition.Scene,
            Content = content
        };
    }

    private sealed class StaticPromptTemplateProvider : IPromptTemplateProvider
    {
        private readonly string _content;

        public StaticPromptTemplateProvider(string content)
        {
            _content = content;
        }

        public Task<PromptTemplateModel> GetOrCreateSystemAsync(
            PromptTemplateScene scene,
            string name,
            string displayName,
            string defaultContent,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PromptTemplateModel
            {
                Id = scene.GetHashCode(),
                Content = _content
            });
        }

        public Task SaveContentAsync(int id, string content, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FixedAiServiceSelector : IAiServiceSelector
    {
        private readonly IReadOnlyList<AiServiceConfigModel> _configs;

        public FixedAiServiceSelector(params AiServiceConfigModel[] configs)
        {
            _configs = configs;
        }

        public Task<IReadOnlyList<AiServiceConfigModel>> GetCandidatesAsync(
            AiServicePurpose purpose,
            int? preferredId = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_configs);
        }
    }

    private sealed class SingleFlightPromptTemplateProvider : IPromptTemplateProvider
    {
        private readonly string _content;
        private int _activeCalls;

        public SingleFlightPromptTemplateProvider(string content)
        {
            _content = content;
        }

        public int CallCount { get; private set; }

        public async Task<PromptTemplateModel> GetOrCreateSystemAsync(
            PromptTemplateScene scene,
            string name,
            string displayName,
            string defaultContent,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (Interlocked.Increment(ref _activeCalls) > 1)
            {
                Interlocked.Decrement(ref _activeCalls);
                throw new InvalidOperationException("PromptTemplateProvider 不允许并发访问");
            }

            try
            {
                await Task.Delay(40, cancellationToken);
                return new PromptTemplateModel
                {
                    Id = scene.GetHashCode(),
                    Content = _content
                };
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        public Task SaveContentAsync(int id, string content, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class SingleFlightAiServiceSelector : IAiServiceSelector
    {
        private readonly IReadOnlyList<AiServiceConfigModel> _configs;
        private int _activeCalls;

        public SingleFlightAiServiceSelector(params AiServiceConfigModel[] configs)
        {
            _configs = configs;
        }

        public int CallCount { get; private set; }

        public async Task<IReadOnlyList<AiServiceConfigModel>> GetCandidatesAsync(
            AiServicePurpose purpose,
            int? preferredId = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (Interlocked.Increment(ref _activeCalls) > 1)
            {
                Interlocked.Decrement(ref _activeCalls);
                throw new InvalidOperationException("AiServiceSelector 不允许并发访问");
            }

            try
            {
                await Task.Delay(40, cancellationToken);
                return _configs;
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }
    }

    private sealed class NonConcurrentDbGate
    {
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeCalls;

        public Task WaitUntilEnteredAsync()
        {
            return _entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public async Task<T> RunAsync<T>(string operation, Func<Task<T>> action, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _activeCalls) > 1)
            {
                Interlocked.Decrement(ref _activeCalls);
                throw new InvalidOperationException($"{operation} 不允许与其他 DB 操作并发");
            }

            _entered.TrySetResult();
            try
            {
                await Task.Delay(120, cancellationToken);
                return await action();
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }
    }

    private sealed class SharedGatePromptTemplateProvider : IPromptTemplateProvider
    {
        private readonly NonConcurrentDbGate _gate;
        private readonly string _content;

        public SharedGatePromptTemplateProvider(NonConcurrentDbGate gate, string content)
        {
            _gate = gate;
            _content = content;
        }

        public int CallCount { get; private set; }

        public Task<PromptTemplateModel> GetOrCreateSystemAsync(
            PromptTemplateScene scene,
            string name,
            string displayName,
            string defaultContent,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return _gate.RunAsync(
                "PromptTemplateProvider",
                () => Task.FromResult(new PromptTemplateModel
                {
                    Id = (int)scene,
                    Content = _content
                }),
                cancellationToken);
        }

        public Task SaveContentAsync(int id, string content, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class SharedGateAiServiceSelector : IAiServiceSelector
    {
        private readonly NonConcurrentDbGate _gate;
        private readonly IReadOnlyList<AiServiceConfigModel> _configs;

        public SharedGateAiServiceSelector(NonConcurrentDbGate gate, params AiServiceConfigModel[] configs)
        {
            _gate = gate;
            _configs = configs;
        }

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<AiServiceConfigModel>> GetCandidatesAsync(
            AiServicePurpose purpose,
            int? preferredId = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return _gate.RunAsync(
                "AiServiceSelector",
                () => Task.FromResult(_configs),
                cancellationToken);
        }
    }

    private sealed class RoutingSemanticKernelServiceFactory : ISemanticKernelServiceFactory
    {
        private readonly IReadOnlyDictionary<int, IChatCompletionService> _chatServices;

        public RoutingSemanticKernelServiceFactory(IReadOnlyDictionary<int, IChatCompletionService> chatServices)
        {
            _chatServices = chatServices;
        }

        public IChatCompletionService CreateChatCompletionService(AiServiceConfigModel config)
        {
            return _chatServices[config.Id];
        }

        public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiServiceConfigModel config)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FixedChatCompletionService : IChatCompletionService
    {
        private readonly string _response;

        public FixedChatCompletionService(string response)
        {
            _response = response;
        }

        public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

        public int CallCount { get; private set; }

        public string LastPrompt { get; private set; } = string.Empty;

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
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
            CallCount++;
            LastPrompt = chatHistory.Last().Content ?? string.Empty;
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, _response);
            await Task.CompletedTask;
        }
    }
}
