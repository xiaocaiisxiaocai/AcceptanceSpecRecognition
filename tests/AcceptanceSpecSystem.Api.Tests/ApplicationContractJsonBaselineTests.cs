using System.Text.Json;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class ApplicationContractJsonBaselineTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void SharedApplicationContracts_ShouldKeepExistingCamelCaseJsonPropertyNames()
    {
        AssertPropertyNames(new FileUploadResponse(),
            "fileId", "fileName", "fileType", "fileHash", "isDuplicate", "tableCount", "tableCountReady");

        AssertPropertyNames(new CreateAuthRoleRequest(),
            "code", "name", "description", "isActive", "permissionCodes", "dataScopes");

        AssertPropertyNames(new CreateOrgUnitRequest(),
            "parentId", "unitType", "code", "name", "sort", "isActive");

        AssertPropertyNames(new CreateSystemUserRequest(),
            "username", "password", "nickname", "avatar", "roleCode", "orgUnitId",
            "roleStartAt", "roleEndAt", "orgStartAt", "orgEndAt", "isActive");

        AssertPropertyNames(new ImportDataRequest(),
            "fileId", "tableIndex", "customerId", "processId", "machineModelId", "mapping",
            "cleanupSourceFile", "previewSkippedRows", "isSpecificationOnly",
            "confirmedDifferenceKeys", "partiallyConfirmedDifferenceKeys", "skippedDifferenceKeys",
            "excludedRowIndexes", "duplicateCheckOptions");

        AssertPropertyNames(new ExcelImportDataRequest(),
            "fileId", "sheetIndex", "customerId", "processId", "machineModelId",
            "headerRowStart", "headerRowCount", "dataStartRow", "dataEndRow",
            "projectColumn", "specificationColumn", "acceptanceColumn", "remarkColumn",
            "cleanupSourceFile", "previewSkippedRows", "isSpecificationOnly",
            "confirmedDifferenceKeys", "partiallyConfirmedDifferenceKeys", "skippedDifferenceKeys",
            "excludedRowIndexes", "duplicateCheckOptions");

        AssertPropertyNames(new CreateColumnMappingRuleRequest(),
            "targetField", "matchMode", "pattern", "priority", "enabled", "source", "customerId");

        AssertPropertyNames(new UpdatePromptTemplateRequest(), "displayName", "content");

        AssertPropertyNames(new CreateSmartStructureRoutingRuleRequest(),
            "name", "tableKind", "recommendation", "matchScope", "matchMode", "pattern",
            "weight", "priority", "enabled", "source", "customerId");

        AssertPropertyNames(new CreateAiServiceRequest(),
            "name", "serviceType", "purpose", "priority", "apiKey", "endpoint",
            "embeddingModel", "llmModel", "disableThinking", "defaultRecallTopK");

        AssertPropertyNames(new AuditLogDetailDto(),
            "id", "source", "level", "eventType", "username", "requestMethod", "requestPath",
            "queryString", "statusCode", "durationMs", "clientIp", "userAgent", "clientTraceId",
            "clientId", "frontendRoute", "createdAt", "details");
    }

    private static void AssertPropertyNames<T>(T value, params string[] expectedNames)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, WebJsonOptions));
        var actualNames = document.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

        actualNames.Should().BeEquivalentTo(expectedNames,
            options => options.WithoutStrictOrdering(),
            $"{typeof(T).Name} 的公开 JSON 属性名属于兼容契约");
    }
}
