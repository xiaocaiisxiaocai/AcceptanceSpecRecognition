using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class DocumentTemplatesApiTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public DocumentTemplatesApiTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetList_ShouldFilterByCustomerAndKeyword()
    {
        var marker = Guid.NewGuid().ToString("N");
        var (customerId, templateId) = await SeedTemplateAsync(
            $"模板客户-{marker}",
            $"工作表-{marker}",
            includeRegions: true);
        await SeedTemplateAsync($"其他客户-{marker}", $"其他表-{marker}", includeRegions: true);

        var response = await _client.GetAsync(
            $"/api/document-templates?page=1&pageSize=20&customerId={customerId}&keyword={marker}");
        var responseText = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
        var data = ReadData(responseText);
        data.GetProperty("total").GetInt32().Should().Be(1);
        var item = data.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("id").GetInt32().Should().Be(templateId);
        item.GetProperty("customerName").GetString().Should().Be($"模板客户-{marker}");
        item.GetProperty("regionCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task GetById_ShouldReturnRegionsInConfiguredOrder()
    {
        var marker = Guid.NewGuid().ToString("N");
        var (_, templateId) = await SeedTemplateAsync(
            $"区域客户-{marker}",
            $"区域表-{marker}",
            includeRegions: true);

        var response = await _client.GetAsync($"/api/document-templates/{templateId}");
        var responseText = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
        var regions = ReadData(responseText).GetProperty("regions").EnumerateArray().ToList();
        regions.Select(item => item.GetProperty("regionIndex").GetInt32())
            .Should().Equal(0, 1);
        regions[0].GetProperty("dataStartRowIndex").GetInt32().Should().Be(8);
        regions[1].GetProperty("dataStartRowIndex").GetInt32().Should().Be(127);
        regions[1].GetProperty("headers").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain("細項");
    }

    [Fact]
    public async Task GetById_WhenLegacyTemplateHasNoRegions_ShouldReturnCompatibleRegion()
    {
        var marker = Guid.NewGuid().ToString("N");
        var (_, templateId) = await SeedTemplateAsync(
            $"旧模板客户-{marker}",
            $"旧模板-{marker}",
            includeRegions: false);

        var response = await _client.GetAsync($"/api/document-templates/{templateId}");
        var responseText = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
        var region = ReadData(responseText).GetProperty("regions").EnumerateArray().Single();
        region.GetProperty("regionIndex").GetInt32().Should().Be(0);
        region.GetProperty("projectColumnIndex").GetInt32().Should().Be(2);
        region.GetProperty("dataStartRowIndex").GetInt32().Should().Be(8);
    }

    [Fact]
    public async Task Delete_ShouldRemoveTemplateAndCascadeRegions()
    {
        var marker = Guid.NewGuid().ToString("N");
        var (_, templateId) = await SeedTemplateAsync(
            $"删除客户-{marker}",
            $"删除表-{marker}",
            includeRegions: true);

        var response = await _client.DeleteAsync($"/api/document-templates/{templateId}");
        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.DocumentTemplates.AnyAsync(item => item.Id == templateId)).Should().BeFalse();
        (await db.DocumentTemplateRegions.AnyAsync(item => item.DocumentTemplateId == templateId)).Should().BeFalse();

        var missingResponse = await _client.GetAsync($"/api/document-templates/{templateId}");
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<(int CustomerId, int TemplateId)> SeedTemplateAsync(
        string customerName,
        string templateName,
        bool includeRegions)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var customer = new Customer { Name = customerName };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var template = new DocumentTemplate
        {
            CustomerId = customer.Id,
            TemplateName = templateName,
            HeadersFingerprint = Guid.NewGuid().ToString("N"),
            HeadersJson = "[\"项目\",\"规格\",\"验收\",\"备注\"]",
            ProjectColumnIndex = 2,
            SpecificationColumnIndex = 3,
            AcceptanceColumnIndex = 8,
            RemarkColumnIndex = 9,
            HeaderRowIndex = 7,
            HeaderRowCount = 1,
            DataStartRowIndex = 8,
            DataEndRowIndex = 111,
            TableKind = "AcceptanceSpec",
            Recommendation = "NeedConfirm",
            ConfirmedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        if (includeRegions)
        {
            template.Regions =
            [
                new DocumentTemplateRegion
                {
                    RegionIndex = 1,
                    HeadersJson = "[\"細項\",\"規格\",\"OK/NG\",\"Remark\"]",
                    HeaderRowIndex = 125,
                    HeaderRowCount = 1,
                    DataStartRowIndex = 127,
                    DataEndRowIndex = 142,
                    ProjectColumnIndex = 2,
                    SpecificationColumnIndex = 3,
                    AcceptanceColumnIndex = 8,
                    RemarkColumnIndex = 9
                },
                new DocumentTemplateRegion
                {
                    RegionIndex = 0,
                    HeadersJson = template.HeadersJson,
                    HeaderRowIndex = 7,
                    HeaderRowCount = 1,
                    DataStartRowIndex = 8,
                    DataEndRowIndex = 111,
                    ProjectColumnIndex = 2,
                    SpecificationColumnIndex = 3,
                    AcceptanceColumnIndex = 8,
                    RemarkColumnIndex = 9
                }
            ];
        }

        db.DocumentTemplates.Add(template);
        await db.SaveChangesAsync();
        return (customer.Id, template.Id);
    }

    private static JsonElement ReadData(string responseText)
    {
        using var document = JsonDocument.Parse(responseText);
        document.RootElement.GetProperty("code").GetInt32().Should().Be(0);
        return document.RootElement.GetProperty("data").Clone();
    }
}

