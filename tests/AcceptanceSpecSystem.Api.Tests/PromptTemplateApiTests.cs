using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class PromptTemplateApiTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PromptTemplateApiTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetList_ShouldReturnSystemPromptTemplates()
    {
        var response = await _client.GetAsync("/api/prompt-templates");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);

        var items = json.Data.GetProperty("items");
        items.GetArrayLength().Should().Be(4);

        var names = items.EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToArray();

        names.Should().Contain("matching-review");
        names.Should().Contain("import-duplicate-review");
        names.Should().Contain("matching-equivalence-adjudication");
        names.Should().Contain("matching-candidate-rerank");
        names.Should().NotContain("matching-entity-resolution");
        names.Should().NotContain("matching-generate");

        var matchingReview = items.EnumerateArray()
            .First(item => item.GetProperty("name").GetString() == "matching-review");

        matchingReview.GetProperty("isSystem").GetBoolean().Should().BeTrue();
        matchingReview.TryGetProperty("isDefault", out _).Should().BeFalse();
        matchingReview.GetProperty("displayName").GetString().Should().NotBeNullOrWhiteSpace();
        matchingReview.GetProperty("availableVariables").GetArrayLength().Should().BeGreaterThan(0);
        matchingReview.GetProperty("availableVariables").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain("workflowScene");
    }

    [Fact]
    public async Task GetList_ShouldNotDeleteLegacyPromptTemplatesFromDatabase()
    {
        await _client.GetAsync("/api/prompt-templates");

        var obsoleteTemplateId = await InsertPromptTemplateAsync(new PromptTemplate
        {
            Name = "matching-generate",
            DisplayName = "历史生成模板",
            Content = "legacy content",
            Scene = PromptTemplateScene.Unknown,
            IsSystem = true
        });

        var customTemplateId = await InsertPromptTemplateAsync(new PromptTemplate
        {
            Name = $"legacy-custom-{Guid.NewGuid():N}"[..20],
            DisplayName = "历史自定义模板",
            Content = "custom content",
            Scene = PromptTemplateScene.Unknown,
            IsSystem = false
        });

        var response = await _client.GetAsync("/api/prompt-templates");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await TemplateExistsAsync(obsoleteTemplateId)).Should().BeTrue();
        (await TemplateExistsAsync(customTemplateId)).Should().BeTrue();
    }

    [Fact]
    public async Task LegacyMutationEndpoints_ShouldNoLongerBeExposed()
    {
        var listResponse = await _client.GetAsync("/api/prompt-templates");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var templateId = listJson.Data.GetProperty("items")
            .EnumerateArray()
            .First()
            .GetProperty("id")
            .GetInt32();

        var defaultResponse = await _client.GetAsync("/api/prompt-templates/default");
        defaultResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var createResponse = await _client.PostAsync(
            "/api/prompt-templates",
            ApiClientJson.ToJsonContent(new
            {
                name = "legacy-template",
                displayName = "legacy-template",
                content = "legacy"
            }));
        createResponse.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);

        var deleteResponse = await _client.DeleteAsync($"/api/prompt-templates/{templateId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);

        var setDefaultResponse = await _client.PostAsync($"/api/prompt-templates/{templateId}/set-default", null);
        setDefaultResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Preview_WhenTemplateMissesRequiredPlaceholder_ShouldReturnValidationErrors()
    {
        var response = await _client.PostAsync(
            "/api/prompt-templates/preview",
            ApiClientJson.ToJsonContent(new
            {
                scene = "matching-review",
                content = "你是复核助手，仅返回 JSON：{\"score\":0}"
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        json.Data.GetProperty("isValid").GetBoolean().Should().BeFalse();

        var errors = json.Data.GetProperty("errors")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

        errors.Should().Contain(error => error!.Contains("sourceProject", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetById_WhenTemplateIsCustomOrLegacySystem_ShouldReturnNotFoundWithoutDeletingRows()
    {
        var obsoleteTemplateId = await InsertPromptTemplateAsync(new PromptTemplate
        {
            Name = "matching-generate",
            DisplayName = "历史生成模板",
            Content = "legacy content",
            Scene = PromptTemplateScene.Unknown,
            IsSystem = true
        });

        var customTemplateId = await InsertPromptTemplateAsync(new PromptTemplate
        {
            Name = $"legacy-custom-{Guid.NewGuid():N}"[..20],
            DisplayName = "历史自定义模板",
            Content = "custom content",
            Scene = PromptTemplateScene.Unknown,
            IsSystem = false
        });

        var obsoleteResponse = await _client.GetAsync($"/api/prompt-templates/{obsoleteTemplateId}");
        var customResponse = await _client.GetAsync($"/api/prompt-templates/{customTemplateId}");

        obsoleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        customResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await TemplateExistsAsync(obsoleteTemplateId)).Should().BeTrue();
        (await TemplateExistsAsync(customTemplateId)).Should().BeTrue();
    }

    [Fact]
    public async Task Update_WhenTemplateContainsUnknownPlaceholder_ShouldReject()
    {
        var listResponse = await _client.GetAsync("/api/prompt-templates");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var templateId = listJson.Data.GetProperty("items")
            .EnumerateArray()
            .First(item => item.GetProperty("name").GetString() == "matching-review")
            .GetProperty("id")
            .GetInt32();

        var updateResponse = await _client.PutAsync(
            $"/api/prompt-templates/{templateId}",
            ApiClientJson.ToJsonContent(new
            {
                displayName = "智能填充复核",
                content = "项目：{{sourceProject}}\n规格：{{unknownToken}}\n仅返回 JSON：{\"score\":0,\"reason\":\"\",\"commentary\":\"\"}"
            }));

        updateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var updateJson = await updateResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        updateJson.Code.Should().Be(400);
        updateJson.Message.Should().Contain("unknownToken");
    }

    [Fact]
    public async Task Update_WhenTemplateIsCustomOrLegacySystem_ShouldRejectByIdContractBypass()
    {
        var obsoleteTemplateId = await InsertPromptTemplateAsync(new PromptTemplate
        {
            Name = "matching-generate",
            DisplayName = "历史生成模板",
            Content = "legacy content",
            Scene = PromptTemplateScene.Unknown,
            IsSystem = true
        });

        var customTemplateId = await InsertPromptTemplateAsync(new PromptTemplate
        {
            Name = $"legacy-custom-{Guid.NewGuid():N}"[..20],
            DisplayName = "历史自定义模板",
            Content = "custom content",
            Scene = PromptTemplateScene.Unknown,
            IsSystem = false
        });

        var obsoleteUpdateResponse = await _client.PutAsync(
            $"/api/prompt-templates/{obsoleteTemplateId}",
            ApiClientJson.ToJsonContent(new
            {
                displayName = "废弃模板",
                content = "项目：{{sourceProject}}\n规格：{{sourceSpecification}}\n仅返回 JSON：{\"score\":0,\"reason\":\"\",\"commentary\":\"\"}"
            }));
        var customUpdateResponse = await _client.PutAsync(
            $"/api/prompt-templates/{customTemplateId}",
            ApiClientJson.ToJsonContent(new
            {
                displayName = "自定义模板",
                content = "项目：{{sourceProject}}\n规格：{{sourceSpecification}}\n仅返回 JSON：{\"score\":0,\"reason\":\"\",\"commentary\":\"\"}"
            }));

        obsoleteUpdateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        customUpdateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var obsoleteTemplate = await GetTemplateAsync(obsoleteTemplateId);
        var customTemplate = await GetTemplateAsync(customTemplateId);
        obsoleteTemplate!.Content.Should().Be("legacy content");
        customTemplate!.Content.Should().Be("custom content");
    }

    [Fact]
    public async Task Update_WhenCustomDisplayNameSaved_ShouldPersistAfterListReload()
    {
        var listResponse = await _client.GetAsync("/api/prompt-templates");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResponse.ReadAsAsync<ApiResponse<JsonElement>>();

        var matchingReview = listJson.Data.GetProperty("items")
            .EnumerateArray()
            .First(item => item.GetProperty("name").GetString() == "matching-review");

        var templateId = matchingReview.GetProperty("id").GetInt32();
        var content = matchingReview.GetProperty("content").GetString();
        var displayName = $"自定义显示名-{Guid.NewGuid():N}"[..12];

        var updateResponse = await _client.PutAsync(
            $"/api/prompt-templates/{templateId}",
            ApiClientJson.ToJsonContent(new
            {
                displayName,
                content
            }));

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updateJson = await updateResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        updateJson.Code.Should().Be(0);
        updateJson.Data.GetProperty("displayName").GetString().Should().Be(displayName);

        var reloadResponse = await _client.GetAsync("/api/prompt-templates");
        reloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reloadJson = await reloadResponse.ReadAsAsync<ApiResponse<JsonElement>>();

        var reloaded = reloadJson.Data.GetProperty("items")
            .EnumerateArray()
            .First(item => item.GetProperty("id").GetInt32() == templateId);

        reloaded.GetProperty("displayName").GetString().Should().Be(displayName);
    }

    private async Task<int> InsertPromptTemplateAsync(PromptTemplate template)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = await db.PromptTemplates.SingleOrDefaultAsync(item => item.Name == template.Name);
        if (existing != null)
        {
            existing.DisplayName = template.DisplayName;
            existing.Content = template.Content;
            existing.Scene = template.Scene;
            existing.IsSystem = template.IsSystem;
            existing.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return existing.Id;
        }

        db.PromptTemplates.Add(template);
        await db.SaveChangesAsync();
        return template.Id;
    }

    private async Task<bool> TemplateExistsAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PromptTemplates.AnyAsync(item => item.Id == id);
    }

    private async Task<PromptTemplate?> GetTemplateAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PromptTemplates.SingleOrDefaultAsync(item => item.Id == id);
    }
}
