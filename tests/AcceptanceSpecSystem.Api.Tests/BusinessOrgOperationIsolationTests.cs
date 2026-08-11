using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class BusinessOrgOperationIsolationTests
{
    [Fact]
    public async Task AdminUpload_WithDepartments_ShouldRequireBusinessOrg_AndPersistSelectedOrg()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedDepartmentsAsync(factory);
        using var client = factory.CreateClient();

        using var missingOrgContent = await CreateUploadContentAsync("admin-missing-org.docx");
        using var missingOrgResponse = await client.PostAsync("/api/documents/upload", missingOrgContent);
        missingOrgResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var rootOrgContent = await CreateUploadContentAsync("admin-root-org.docx");
        rootOrgContent.Add(
            new StringContent(fixture.RootOrgUnitId.ToString()),
            "businessOrgUnitId");
        using var rootOrgResponse = await client.PostAsync("/api/documents/upload", rootOrgContent);
        rootOrgResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var selectedOrgContent = await CreateUploadContentAsync("admin-department-a.docx");
        selectedOrgContent.Add(
            new StringContent(fixture.DepartmentAId.ToString()),
            "businessOrgUnitId");
        using var selectedOrgResponse = await client.PostAsync("/api/documents/upload", selectedOrgContent);
        selectedOrgResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await selectedOrgResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Data.GetProperty("ownerOrgUnitId").GetInt32().Should().Be(fixture.DepartmentAId);
        body.Data.GetProperty("ownerOrgUnitName").GetString().Should().Be("A部门");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fileId = body.Data.GetProperty("fileId").GetInt32();
        var ownerOrgUnitId = await db.WordFiles
            .Where(file => file.Id == fileId)
            .Select(file => file.OwnerOrgUnitId)
            .SingleAsync();
        ownerOrgUnitId.Should().Be(fixture.DepartmentAId);
    }

    [Fact]
    public async Task CommonUpload_ShouldUseOwnOrg_AndRejectOtherDepartment()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedDepartmentsAsync(factory);
        using var client = factory.CreateClient();

        using var ownOrgRequest = CreateCommonUploadRequest(
            fixture.CommonUserId,
            await CreateUploadContentAsync("common-own-org.docx"));
        using var ownOrgResponse = await client.SendAsync(ownOrgRequest);
        ownOrgResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownOrgBody = await ownOrgResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        ownOrgBody.Data.GetProperty("ownerOrgUnitId").GetInt32().Should().Be(fixture.DepartmentAId);

        var otherOrgContent = await CreateUploadContentAsync("common-other-org.docx");
        otherOrgContent.Add(
            new StringContent(fixture.DepartmentBId.ToString()),
            "businessOrgUnitId");
        using var otherOrgRequest = CreateCommonUploadRequest(fixture.CommonUserId, otherOrgContent);
        using var otherOrgResponse = await client.SendAsync(otherOrgRequest);
        otherOrgResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BusinessContext_ShouldReturnAdminChoices_AndCommonLockedOrg()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedDepartmentsAsync(factory);
        using var client = factory.CreateClient();

        using var adminResponse = await client.GetAsync("/api/org-units/business-context");
        adminResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var adminBody = await adminResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        adminBody.Data.GetProperty("requiresSelection").GetBoolean().Should().BeTrue();
        adminBody.Data.GetProperty("options").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt32())
            .Should().Contain([fixture.DepartmentAId, fixture.DepartmentBId]);

        using var commonRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/org-units/business-context");
        commonRequest.Headers.Add("X-Test-Role", "common");
        commonRequest.Headers.Add("X-Test-User-Id", fixture.CommonUserId.ToString());
        commonRequest.Headers.Add("X-Test-Permissions", "*:*:*");
        using var commonResponse = await client.SendAsync(commonRequest);
        commonResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var commonBody = await commonResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        commonBody.Data.GetProperty("requiresSelection").GetBoolean().Should().BeFalse();
        commonBody.Data.GetProperty("currentOrgUnitId").GetInt32().Should().Be(fixture.DepartmentAId);
        commonBody.Data.GetProperty("currentOrgUnitName").GetString().Should().Be("A部门");
    }

    [Fact]
    public async Task SpecReadScope_ShouldLetAdminFilterDepartment_AndRejectCommonOverride()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedDepartmentsAsync(factory);
        var (departmentAProject, departmentBProject) =
            await SeedDepartmentSpecsAsync(factory, fixture);
        using var client = factory.CreateClient();

        using var adminListResponse = await client.GetAsync(
            $"/api/specs?page=1&pageSize=20&orgUnitId={fixture.DepartmentAId}");
        adminListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var adminListBody = await adminListResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var adminProjects = adminListBody.Data
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("project").GetString())
            .ToList();
        adminProjects.Should().Contain(departmentAProject).And.NotContain(departmentBProject);
        adminListBody.Data.GetProperty("total").GetInt32().Should().Be(1);

        using var adminGroupsResponse = await client.GetAsync(
            $"/api/specs/groups?orgUnitId={fixture.DepartmentAId}");
        adminGroupsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var adminGroupsBody = await adminGroupsResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        adminGroupsBody.Data.EnumerateArray()
            .Sum(item => item.GetProperty("specCount").GetInt32())
            .Should().Be(1);

        using var commonRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/specs?page=1&pageSize=20&orgUnitId={fixture.DepartmentBId}");
        commonRequest.Headers.Add("X-Test-Role", "common");
        commonRequest.Headers.Add("X-Test-User-Id", fixture.CommonUserId.ToString());
        commonRequest.Headers.Add("X-Test-Permissions", "*:*:*");
        using var commonResponse = await client.SendAsync(commonRequest);
        commonResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SpecSearches_ShouldKeepKeywordDuplicateAndSemanticResultsInsideDepartment()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedDepartmentsAsync(factory);
        var searchFixture = await SeedDepartmentSearchSpecsAsync(factory, fixture);
        using var client = factory.CreateClient();

        using var keywordResponse = await client.GetAsync(
            $"/api/specs?page=1&pageSize=20&keyword={Uri.EscapeDataString(searchFixture.Keyword)}" +
            $"&orgUnitId={fixture.DepartmentAId}");
        keywordResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var keywordBody = await keywordResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        keywordBody.Data.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt32())
            .Should().BeEquivalentTo(searchFixture.DepartmentASpecIds);

        using var duplicateResponse = await client.GetAsync(
            $"/api/specs/duplicate-groups?customerId={searchFixture.CustomerId}" +
            $"&orgUnitId={fixture.DepartmentAId}");
        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var duplicateBody = await duplicateResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var duplicateIds = duplicateBody.Data.GetProperty("exactGroups")
            .EnumerateArray()
            .SelectMany(group => group.GetProperty("items").EnumerateArray())
            .Select(item => item.GetProperty("id").GetInt32())
            .ToList();
        duplicateIds.Should().BeEquivalentTo(searchFixture.DepartmentASpecIds);

        using var semanticResponse = await client.PostAsync(
            "/api/specs/semantic-search",
            ApiClientJson.ToJsonContent(new
            {
                orgUnitId = fixture.DepartmentAId,
                customerId = searchFixture.CustomerId,
                queries = new[] { searchFixture.Keyword },
                topK = 20,
                minScore = 0
            }));
        semanticResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var semanticBody = await semanticResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        semanticBody.Data.GetProperty("groups")[0].GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt32())
            .Should().BeEquivalentTo(searchFixture.DepartmentASpecIds);

        using var commonDuplicateRequest = CreateCommonRequest(
            fixture.CommonUserId,
            HttpMethod.Get,
            $"/api/specs/duplicate-groups?orgUnitId={fixture.DepartmentBId}");
        using var commonDuplicateResponse = await client.SendAsync(commonDuplicateRequest);
        commonDuplicateResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var commonSemanticRequest = CreateCommonRequest(
            fixture.CommonUserId,
            HttpMethod.Post,
            "/api/specs/semantic-search",
            new
            {
                orgUnitId = fixture.DepartmentBId,
                queries = new[] { searchFixture.Keyword },
                topK = 5,
                minScore = 0
            });
        using var commonSemanticResponse = await client.SendAsync(commonSemanticRequest);
        commonSemanticResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RemarkBatchReplace_ShouldRequireDepartment_ReplaceOnlyItsRows_AndClearItsCaches()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedDepartmentsAsync(factory);
        var replaceFixture = await SeedRemarkReplaceSpecsAsync(factory, fixture);
        using var client = factory.CreateClient();

        var previewPayload = new
        {
            searchText = replaceFixture.SearchText,
            replacementText = replaceFixture.ReplacementText
        };
        using var overallResponse = await client.PostAsync(
            "/api/specs/remark-replace/preview",
            ApiClientJson.ToJsonContent(previewPayload));
        overallResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var previewResponse = await client.PostAsync(
            "/api/specs/remark-replace/preview",
            ApiClientJson.ToJsonContent(new
            {
                orgUnitId = fixture.DepartmentAId,
                previewPayload.searchText,
                previewPayload.replacementText
            }));
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewBody = await previewResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        previewBody.Data.GetProperty("affectedSpecCount").GetInt32().Should().Be(2);
        previewBody.Data.GetProperty("matchCount").GetInt32().Should().Be(3);
        previewBody.Data.GetProperty("samples").GetArrayLength().Should().Be(2);
        var confirmationToken = previewBody.Data.GetProperty("confirmationToken").GetString();
        confirmationToken.Should().NotBeNullOrWhiteSpace();

        using var tamperedExecuteResponse = await client.PostAsync(
            "/api/specs/remark-replace",
            ApiClientJson.ToJsonContent(new
            {
                orgUnitId = fixture.DepartmentAId,
                previewPayload.searchText,
                previewPayload.replacementText,
                expectedAffectedSpecCount = 2,
                expectedMatchCount = 3,
                confirmationToken = "tampered"
            }));
        tamperedExecuteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var executeResponse = await client.PostAsync(
            "/api/specs/remark-replace",
            ApiClientJson.ToJsonContent(new
            {
                orgUnitId = fixture.DepartmentAId,
                previewPayload.searchText,
                previewPayload.replacementText,
                expectedAffectedSpecCount = 2,
                expectedMatchCount = 3,
                confirmationToken
            }));
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var executeBody = await executeResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        executeBody.Data.GetProperty("updatedSpecCount").GetInt32().Should().Be(2);
        executeBody.Data.GetProperty("replacedMatchCount").GetInt32().Should().Be(3);

        await using (var verifyScope = factory.Services.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var remarks = await db.AcceptanceSpecs
                .Where(spec => replaceFixture.AllSpecIds.Contains(spec.Id))
                .ToDictionaryAsync(spec => spec.Id, spec => spec.Remark);
            remarks[replaceFixture.DepartmentASpecIds[0]].Should().Be("新字段 / 新字段");
            remarks[replaceFixture.DepartmentASpecIds[1]].Should().Be("仅新字段");
            remarks[replaceFixture.DepartmentBSpecId].Should().Be("旧字段 / B部门");

            var replacementVersions = await db.AcceptanceSpecContentVersions
                .Where(version => replaceFixture.DepartmentASpecIds.Contains(version.AcceptanceSpecId))
                .ToListAsync();
            replacementVersions.Should().HaveCount(2);
            replacementVersions.Should().OnlyContain(version =>
                version.Version == 2 && version.ChangeSource == "remark-replace");

            var remainingCacheSpecIds = await db.EmbeddingCaches
                .Where(cache => replaceFixture.AllSpecIds.Contains(cache.SpecId))
                .Select(cache => cache.SpecId)
                .ToListAsync();
            remainingCacheSpecIds.Should().BeEquivalentTo([replaceFixture.DepartmentBSpecId]);

            var audit = await db.AuditLogs
                .Where(item =>
                    item.EventType == "controller.remark-replace" &&
                    item.Details != null &&
                    item.Details.Contains("updatedSpecCount"))
                .OrderByDescending(item => item.Id)
                .FirstAsync();
            audit.Details.Should().Contain($"\"orgUnitId\":{fixture.DepartmentAId}");
            audit.Details.Should().Contain("\"updatedSpecCount\":2");
            audit.Details.Should().Contain("\"replacedMatchCount\":3");
            audit.Details.Should().NotContain(replaceFixture.SearchText);
            audit.Details.Should().NotContain(replaceFixture.ReplacementText);
        }

        using var commonOwnPreviewRequest = CreateCommonRequest(
            fixture.CommonUserId,
            HttpMethod.Post,
            "/api/specs/remark-replace/preview",
            new
            {
                orgUnitId = fixture.DepartmentAId,
                searchText = replaceFixture.ReplacementText,
                replacementText = "再次替换"
            });
        using var commonOwnPreviewResponse = await client.SendAsync(commonOwnPreviewRequest);
        commonOwnPreviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var commonOtherPreviewRequest = CreateCommonRequest(
            fixture.CommonUserId,
            HttpMethod.Post,
            "/api/specs/remark-replace/preview",
            new
            {
                orgUnitId = fixture.DepartmentBId,
                searchText = replaceFixture.SearchText,
                replacementText = replaceFixture.ReplacementText
            });
        using var commonOtherPreviewResponse = await client.SendAsync(commonOtherPreviewRequest);
        commonOtherPreviewResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RemarkBatchReplace_ShouldRejectStalePreviewWithoutPartialWrite()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedDepartmentsAsync(factory);
        var replaceFixture = await SeedRemarkReplaceSpecsAsync(factory, fixture);
        using var client = factory.CreateClient();

        using var previewResponse = await client.PostAsync(
            "/api/specs/remark-replace/preview",
            ApiClientJson.ToJsonContent(new
            {
                orgUnitId = fixture.DepartmentAId,
                searchText = replaceFixture.SearchText,
                replacementText = replaceFixture.ReplacementText
            }));
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewBody = await previewResponse.ReadAsAsync<ApiResponse<JsonElement>>();

        await using (var mutateScope = factory.Services.CreateAsyncScope())
        {
            var db = mutateScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var spec = await db.AcceptanceSpecs.FindAsync(replaceFixture.DepartmentASpecIds[0]);
            spec!.Remark = $"{spec.Remark} / 新增匹配{replaceFixture.SearchText}";
            spec.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        using var executeResponse = await client.PostAsync(
            "/api/specs/remark-replace",
            ApiClientJson.ToJsonContent(new
            {
                orgUnitId = fixture.DepartmentAId,
                searchText = replaceFixture.SearchText,
                replacementText = replaceFixture.ReplacementText,
                expectedAffectedSpecCount = previewBody.Data.GetProperty("affectedSpecCount").GetInt32(),
                expectedMatchCount = previewBody.Data.GetProperty("matchCount").GetInt32(),
                confirmationToken = previewBody.Data.GetProperty("confirmationToken").GetString()
            }));
        executeResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var remarks = await verifyDb.AcceptanceSpecs
            .Where(spec => replaceFixture.DepartmentASpecIds.Contains(spec.Id))
            .Select(spec => spec.Remark)
            .ToListAsync();
        remarks.Should().OnlyContain(remark => remark!.Contains(replaceFixture.SearchText));
    }

    [Fact]
    public async Task RemarkBatchReplacePreview_ShouldPageThroughAllAffectedSpecs()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedDepartmentsAsync(factory);
        var searchText = $"分页预览{Guid.NewGuid():N}";
        await SeedRemarkReplacePreviewPageAsync(factory, fixture.DepartmentAId, searchText, 12);
        using var client = factory.CreateClient();

        using var firstResponse = await client.PostAsync(
            "/api/specs/remark-replace/preview",
            ApiClientJson.ToJsonContent(new
            {
                orgUnitId = fixture.DepartmentAId,
                searchText,
                replacementText = "已替换",
                page = 1,
                pageSize = 10
            }));
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await firstResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        firstBody.Data.GetProperty("affectedSpecCount").GetInt32().Should().Be(12);
        firstBody.Data.GetProperty("sampleTotal").GetInt32().Should().Be(12);
        firstBody.Data.GetProperty("samplePage").GetInt32().Should().Be(1);
        firstBody.Data.GetProperty("samplePageSize").GetInt32().Should().Be(10);
        firstBody.Data.GetProperty("samples").GetArrayLength().Should().Be(10);

        using var secondResponse = await client.PostAsync(
            "/api/specs/remark-replace/preview",
            ApiClientJson.ToJsonContent(new
            {
                orgUnitId = fixture.DepartmentAId,
                searchText,
                replacementText = "已替换",
                page = 2,
                pageSize = 10
            }));
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await secondResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        secondBody.Data.GetProperty("samplePage").GetInt32().Should().Be(2);
        secondBody.Data.GetProperty("samples").GetArrayLength().Should().Be(2);
        secondBody.Data.GetProperty("confirmationToken").GetString()
            .Should().Be(firstBody.Data.GetProperty("confirmationToken").GetString());
    }

    [Fact]
    public async Task ManualSpecCreate_ShouldRequireAdminDepartment_AndPersistSelectedOrg()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedDepartmentsAsync(factory);
        using var client = factory.CreateClient();

        int customerId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = new Customer
            {
                Name = $"手工新增客户-{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow
            };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
        }

        var baseRequest = new
        {
            customerId,
            project = $"手工新增-{Guid.NewGuid():N}",
            specification = "仅属于A部门的规格"
        };

        using var missingOrgResponse = await client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(baseRequest));
        missingOrgResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var rootOrgResponse = await client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                businessOrgUnitId = fixture.RootOrgUnitId,
                baseRequest.customerId,
                baseRequest.project,
                baseRequest.specification
            }));
        rootOrgResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var selectedOrgResponse = await client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                businessOrgUnitId = fixture.DepartmentAId,
                baseRequest.customerId,
                baseRequest.project,
                baseRequest.specification
            }));
        selectedOrgResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var selectedOrgBody = await selectedOrgResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        selectedOrgBody.Data.GetProperty("ownerOrgUnitId").GetInt32()
            .Should().Be(fixture.DepartmentAId);

        using var commonRequest = new HttpRequestMessage(HttpMethod.Post, "/api/specs")
        {
            Content = ApiClientJson.ToJsonContent(new
            {
                businessOrgUnitId = fixture.DepartmentBId,
                baseRequest.customerId,
                project = $"普通用户越权-{Guid.NewGuid():N}",
                baseRequest.specification
            })
        };
        commonRequest.Headers.Add("X-Test-Role", "common");
        commonRequest.Headers.Add("X-Test-User-Id", fixture.CommonUserId.ToString());
        commonRequest.Headers.Add("X-Test-Permissions", "*:*:*");
        using var commonResponse = await client.SendAsync(commonRequest);
        commonResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminBackfill_ShouldUseSourceFileOrg_AndRejectCrossDepartmentSpec()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedDepartmentsAsync(factory);
        using var client = factory.CreateClient();

        using var uploadContent = await CreateUploadContentAsync("admin-backfill-a.docx");
        uploadContent.Add(
            new StringContent(fixture.DepartmentAId.ToString()),
            "businessOrgUnitId");
        using var uploadResponse = await client.PostAsync("/api/documents/upload", uploadContent);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploadBody = await uploadResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var sourceFileId = uploadBody.Data.GetProperty("fileId").GetInt32();

        int customerId;
        int departmentBSpecId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var adminId = await db.SystemUsers
                .Where(user => user.Username == "admin")
                .Select(user => user.Id)
                .SingleAsync();
            var customer = new Customer
            {
                Name = $"隔离客户-{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow
            };
            var departmentBFile = new WordFile
            {
                FileName = "department-b.docx",
                FilePath = "department-b.docx",
                FileHash = Guid.NewGuid().ToString("N"),
                FileType = UploadedFileType.WordDocx,
                FileContent = [1],
                CreatedByUserId = adminId,
                CompanyId = 1,
                OwnerOrgUnitId = fixture.DepartmentBId,
                UploadedAt = DateTime.UtcNow
            };
            db.AddRange(customer, departmentBFile);
            await db.SaveChangesAsync();
            var departmentBSpec = new AcceptanceSpec
            {
                CustomerId = customer.Id,
                Project = "B部门项目",
                Specification = "B部门规格",
                Acceptance = "B部门验收",
                WordFileId = departmentBFile.Id,
                CreatedByUserId = adminId,
                OwnerOrgUnitId = fixture.DepartmentBId,
                ImportedAt = DateTime.UtcNow
            };
            db.AcceptanceSpecs.Add(departmentBSpec);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            departmentBSpecId = departmentBSpec.Id;
        }

        using var crossDepartmentResponse = await client.PostAsync(
            "/api/matching/spec-backfill",
            ApiClientJson.ToJsonContent(new
            {
                fileId = sourceFileId,
                customerId,
                items = new[]
                {
                    new
                    {
                        specId = (int?)departmentBSpecId,
                        overrideAcceptance = "不应修改"
                    }
                }
            }));
        crossDepartmentResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var project = $"A部门新增-{Guid.NewGuid():N}";
        using var createResponse = await client.PostAsync(
            "/api/matching/spec-backfill",
            ApiClientJson.ToJsonContent(new
            {
                fileId = sourceFileId,
                customerId,
                items = new[]
                {
                    new
                    {
                        specId = (int?)null,
                        sourceProject = project,
                        sourceSpecification = "A部门规格",
                        overrideAcceptance = "A部门验收"
                    }
                }
            }));
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var createdOwner = await verifyDb.AcceptanceSpecs
            .Where(spec => spec.Project == project)
            .Select(spec => spec.OwnerOrgUnitId)
            .SingleAsync();
        createdOwner.Should().Be(fixture.DepartmentAId);

        var adminIdForScope = await verifyDb.SystemUsers
            .Where(user => user.Username == "admin")
            .Select(user => user.Id)
            .SingleAsync();
        var sourceFile = await verifyDb.WordFiles.SingleAsync(file => file.Id == sourceFileId);
        var authScopeService = verifyScope.ServiceProvider
            .GetRequiredService<IAuthDataScopeService>();
        var businessScopeService = verifyScope.ServiceProvider
            .GetRequiredService<IBusinessOrgScopeService>();
        var candidateProvider = verifyScope.ServiceProvider
            .GetRequiredService<MatchingCandidateProvider>();
        var adminScope = await authScopeService.GetScopeAsync(
            adminIdForScope,
            1,
            "spec");
        adminScope.Should().NotBeNull();
        var businessScope = await businessScopeService.ResolveFileScopeAsync(
            adminScope!,
            sourceFile);
        var candidates = await candidateProvider.GetCandidatesAsync(
            customerId,
            null,
            null,
            businessScope,
            null,
            hydrateEmbeddings: false);
        candidates.Select(candidate => candidate.Project)
            .Should().Contain(project)
            .And.NotContain("B部门项目");
    }

    [Fact]
    public async Task AdminImport_ShouldPersistSpecsToSelectedBusinessOrg()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedDepartmentsAsync(factory);
        using var client = factory.CreateClient();
        var project = $"导入A项目-{Guid.NewGuid():N}";
        var customerId = 0;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = new Customer
            {
                Name = $"导入客户-{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow
            };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
        }

        using var uploadContent = new MultipartFormDataContent();
        var excelContent = new ByteArrayContent(CreateExcelBytes(project));
        excelContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        uploadContent.Add(excelContent, "file", "department-a-import.xlsx");
        uploadContent.Add(
            new StringContent(fixture.DepartmentAId.ToString()),
            "businessOrgUnitId");
        using var uploadResponse = await client.PostAsync("/api/documents/upload", uploadContent);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploadBody = await uploadResponse.ReadAsAsync<ApiResponse<JsonElement>>();

        using var importResponse = await client.PostAsync(
            "/api/documents/excel/import",
            ApiClientJson.ToJsonContent(new
            {
                fileId = uploadBody.Data.GetProperty("fileId").GetInt32(),
                sheetIndex = 0,
                customerId,
                headerRowStart = 1,
                headerRowCount = 1,
                dataStartRow = 2,
                dataEndRow = 2,
                projectColumn = 1,
                specificationColumn = 2,
                acceptanceColumn = 3,
                remarkColumn = 4
            }));
        importResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownerOrgUnitId = await verifyDb.AcceptanceSpecs
            .Where(spec => spec.Project == project)
            .Select(spec => spec.OwnerOrgUnitId)
            .SingleAsync();
        ownerOrgUnitId.Should().Be(fixture.DepartmentAId);
    }

    [Fact]
    public async Task LegacyBatchImport_ShouldInheritSourceFileOrg_AndRejectCommonCrossDepartment()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedDepartmentsAsync(factory);
        using var client = factory.CreateClient();

        int customerId;
        int departmentBFileId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var adminId = await db.SystemUsers
                .Where(user => user.Username == "admin")
                .Select(user => user.Id)
                .SingleAsync();
            var customer = new Customer
            {
                Name = $"旧批量导入客户-{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow
            };
            var departmentBFile = new WordFile
            {
                FileName = "legacy-batch-import-b.docx",
                FilePath = "legacy-batch-import-b.docx",
                FileHash = Guid.NewGuid().ToString("N"),
                FileType = UploadedFileType.WordDocx,
                FileContent = [1],
                CreatedByUserId = adminId,
                CompanyId = 1,
                OwnerOrgUnitId = fixture.DepartmentBId,
                UploadedAt = DateTime.UtcNow
            };
            db.AddRange(customer, departmentBFile);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            departmentBFileId = departmentBFile.Id;
        }

        var adminProject = $"旧批量导入B部门-{Guid.NewGuid():N}";
        var payload = new
        {
            customerId,
            wordFileId = departmentBFileId,
            items = new[]
            {
                new
                {
                    project = adminProject,
                    specification = "必须继承B部门归属"
                }
            }
        };
        using var adminResponse = await client.PostAsync(
            "/api/specs/batch-import",
            ApiClientJson.ToJsonContent(payload));
        adminResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var verifyScope = factory.Services.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ownerOrgUnitId = await db.AcceptanceSpecs
                .Where(spec => spec.Project == adminProject)
                .Select(spec => spec.OwnerOrgUnitId)
                .SingleAsync();
            ownerOrgUnitId.Should().Be(fixture.DepartmentBId);
        }

        using var commonRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/specs/batch-import")
        {
            Content = ApiClientJson.ToJsonContent(new
            {
                customerId,
                wordFileId = departmentBFileId,
                items = new[]
                {
                    new
                    {
                        project = $"普通用户跨部门导入-{Guid.NewGuid():N}",
                        specification = "不应写入"
                    }
                }
            })
        };
        commonRequest.Headers.Add("X-Test-Role", "common");
        commonRequest.Headers.Add("X-Test-User-Id", fixture.CommonUserId.ToString());
        commonRequest.Headers.Add("X-Test-Permissions", "*:*:*");
        using var commonResponse = await client.SendAsync(commonRequest);
        commonResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static HttpRequestMessage CreateCommonUploadRequest(
        int userId,
        MultipartFormDataContent content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/documents/upload")
        {
            Content = content
        };
        request.Headers.Add("X-Test-Role", "common");
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        request.Headers.Add("X-Test-Permissions", "*:*:*");
        return request;
    }

    private static HttpRequestMessage CreateCommonRequest(
        int userId,
        HttpMethod method,
        string path,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body != null)
        {
            request.Content = ApiClientJson.ToJsonContent(body);
        }
        request.Headers.Add("X-Test-Role", "common");
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        request.Headers.Add("X-Test-Permissions", "*:*:*");
        return request;
    }

    private static async Task<MultipartFormDataContent> CreateUploadContentAsync(string fileName)
    {
        var docPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "example.docx"));
        var bytes = await File.ReadAllBytesAsync(docPath);
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        content.Add(fileContent, "file", fileName);
        return content;
    }

    private static async Task<DepartmentFixture> SeedDepartmentsAsync(
        ApiWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var root = await db.OrgUnits.SingleAsync(org => org.ParentId == null);
        var commonUser = await db.SystemUsers.SingleAsync(user => user.Username == "common");

        var departmentA = CreateDepartment(root, "A部门", now);
        var departmentB = CreateDepartment(root, "B部门", now);
        db.OrgUnits.AddRange(departmentA, departmentB);
        await db.SaveChangesAsync();
        departmentA.Path = $"{root.Path}{departmentA.Id}/";
        departmentB.Path = $"{root.Path}{departmentB.Id}/";

        db.AuthUserOrgUnits.RemoveRange(
            db.AuthUserOrgUnits.Where(link => link.UserId == commonUser.Id));
        db.AuthUserOrgUnits.Add(new AuthUserOrgUnit
        {
            UserId = commonUser.Id,
            OrgUnitId = departmentA.Id,
            IsPrimary = true,
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        return new DepartmentFixture(root.Id, commonUser.Id, departmentA.Id, departmentB.Id);
    }

    private static async Task<(string DepartmentAProject, string DepartmentBProject)>
        SeedDepartmentSpecsAsync(
            ApiWebApplicationFactory factory,
            DepartmentFixture fixture)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminId = await db.SystemUsers
            .Where(user => user.Username == "admin")
            .Select(user => user.Id)
            .SingleAsync();
        var customer = new Customer
        {
            Name = $"部门筛选客户-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow
        };
        var fileA = new WordFile
        {
            FileName = "spec-scope-a.docx",
            FilePath = "spec-scope-a.docx",
            FileHash = Guid.NewGuid().ToString("N"),
            FileType = UploadedFileType.WordDocx,
            FileContent = [1],
            CreatedByUserId = adminId,
            CompanyId = 1,
            OwnerOrgUnitId = fixture.DepartmentAId,
            UploadedAt = DateTime.UtcNow
        };
        var fileB = new WordFile
        {
            FileName = "spec-scope-b.docx",
            FilePath = "spec-scope-b.docx",
            FileHash = Guid.NewGuid().ToString("N"),
            FileType = UploadedFileType.WordDocx,
            FileContent = [1],
            CreatedByUserId = adminId,
            CompanyId = 1,
            OwnerOrgUnitId = fixture.DepartmentBId,
            UploadedAt = DateTime.UtcNow
        };
        db.AddRange(customer, fileA, fileB);
        await db.SaveChangesAsync();

        var projectA = $"A部门筛选-{Guid.NewGuid():N}";
        var projectB = $"B部门筛选-{Guid.NewGuid():N}";
        db.AcceptanceSpecs.AddRange(
            new AcceptanceSpec
            {
                CustomerId = customer.Id,
                Project = projectA,
                Specification = "A部门规格",
                WordFileId = fileA.Id,
                CreatedByUserId = adminId,
                OwnerOrgUnitId = fixture.DepartmentAId,
                ImportedAt = DateTime.UtcNow
            },
            new AcceptanceSpec
            {
                CustomerId = customer.Id,
                Project = projectB,
                Specification = "B部门规格",
                WordFileId = fileB.Id,
                CreatedByUserId = adminId,
                OwnerOrgUnitId = fixture.DepartmentBId,
                ImportedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
        return (projectA, projectB);
    }

    private static async Task<DepartmentSearchFixture> SeedDepartmentSearchSpecsAsync(
        ApiWebApplicationFactory factory,
        DepartmentFixture fixture)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminId = await db.SystemUsers
            .Where(user => user.Username == "admin")
            .Select(user => user.Id)
            .SingleAsync();
        var keyword = $"部门搜索隔离{Guid.NewGuid():N}";
        var customer = new Customer
        {
            Name = $"搜索隔离客户-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow
        };
        var fileA = CreateOwnedWordFile("search-a.docx", adminId, fixture.DepartmentAId);
        var fileB = CreateOwnedWordFile("search-b.docx", adminId, fixture.DepartmentBId);
        db.AddRange(customer, fileA, fileB);
        await db.SaveChangesAsync();

        var specs = new[]
        {
            CreateOwnedSpec(customer.Id, fileA.Id, adminId, fixture.DepartmentAId, keyword, "相同规格", $"{keyword} A1"),
            CreateOwnedSpec(customer.Id, fileA.Id, adminId, fixture.DepartmentAId, keyword, "相同规格", $"{keyword} A2"),
            CreateOwnedSpec(customer.Id, fileB.Id, adminId, fixture.DepartmentBId, keyword, "相同规格", $"{keyword} B")
        };
        db.AcceptanceSpecs.AddRange(specs);
        await db.SaveChangesAsync();
        return new DepartmentSearchFixture(
            customer.Id,
            keyword,
            specs.Take(2).Select(spec => spec.Id).ToArray(),
            specs[2].Id);
    }

    private static async Task<RemarkReplaceFixture> SeedRemarkReplaceSpecsAsync(
        ApiWebApplicationFactory factory,
        DepartmentFixture fixture)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminId = await db.SystemUsers
            .Where(user => user.Username == "admin")
            .Select(user => user.Id)
            .SingleAsync();
        var customer = new Customer
        {
            Name = $"备注替换客户-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow
        };
        var fileA = CreateOwnedWordFile("remark-replace-a.docx", adminId, fixture.DepartmentAId);
        var fileB = CreateOwnedWordFile("remark-replace-b.docx", adminId, fixture.DepartmentBId);
        db.AddRange(customer, fileA, fileB);
        await db.SaveChangesAsync();

        var specs = new[]
        {
            CreateOwnedSpec(customer.Id, fileA.Id, adminId, fixture.DepartmentAId, "A1", "规格", "旧字段 / 旧字段"),
            CreateOwnedSpec(customer.Id, fileA.Id, adminId, fixture.DepartmentAId, "A2", "规格", "仅旧字段"),
            CreateOwnedSpec(customer.Id, fileB.Id, adminId, fixture.DepartmentBId, "B", "规格", "旧字段 / B部门")
        };
        db.AcceptanceSpecs.AddRange(specs);
        await db.SaveChangesAsync();
        db.EmbeddingCaches.AddRange(specs.Select(spec => new EmbeddingCache
        {
            SpecId = spec.Id,
            ModelName = "remark-replace-test",
            Usage = "semantic-search",
            TextHash = Guid.NewGuid().ToString("N"),
            Vector = [1, 2, 3],
            CreatedAt = DateTime.UtcNow
        }));
        await db.SaveChangesAsync();
        return new RemarkReplaceFixture(
            "旧字段",
            "新字段",
            specs.Take(2).Select(spec => spec.Id).ToArray(),
            specs[2].Id);
    }

    private static async Task SeedRemarkReplacePreviewPageAsync(
        ApiWebApplicationFactory factory,
        int orgUnitId,
        string searchText,
        int count)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminId = await db.SystemUsers
            .Where(user => user.Username == "admin")
            .Select(user => user.Id)
            .SingleAsync();
        var customer = new Customer
        {
            Name = $"备注分页客户-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow
        };
        var file = CreateOwnedWordFile("remark-preview-page.docx", adminId, orgUnitId);
        db.AddRange(customer, file);
        await db.SaveChangesAsync();

        var specs = Enumerable.Range(1, count)
            .Select(index => CreateOwnedSpec(
                customer.Id,
                file.Id,
                adminId,
                orgUnitId,
                $"分页项目{index:D2}",
                "规格",
                $"备注包含 {searchText}"))
            .ToArray();
        db.AcceptanceSpecs.AddRange(specs);
        await db.SaveChangesAsync();
    }

    private static WordFile CreateOwnedWordFile(
        string fileName,
        int userId,
        int orgUnitId) => new()
        {
            FileName = fileName,
            FilePath = fileName,
            FileHash = Guid.NewGuid().ToString("N"),
            FileType = UploadedFileType.WordDocx,
            FileContent = [1],
            CreatedByUserId = userId,
            CompanyId = 1,
            OwnerOrgUnitId = orgUnitId,
            UploadedAt = DateTime.UtcNow
        };

    private static AcceptanceSpec CreateOwnedSpec(
        int customerId,
        int wordFileId,
        int userId,
        int orgUnitId,
        string project,
        string specification,
        string remark) => new()
        {
            CustomerId = customerId,
            Project = project,
            Specification = specification,
            Remark = remark,
            WordFileId = wordFileId,
            CreatedByUserId = userId,
            OwnerOrgUnitId = orgUnitId,
            ImportedAt = DateTime.UtcNow
        };

    private static OrgUnit CreateDepartment(OrgUnit root, string name, DateTime now) => new()
    {
        CompanyId = root.CompanyId,
        ParentId = root.Id,
        UnitType = OrgUnitType.Department,
        Code = $"{name}-{Guid.NewGuid():N}"[..28],
        Name = name,
        Path = "/",
        Depth = root.Depth + 1,
        IsActive = true,
        CreatedAt = now
    };

    private static byte[] CreateExcelBytes(string project)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Sheet1");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "验收";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = project;
        worksheet.Cell(2, 2).Value = "A部门规格";
        worksheet.Cell(2, 3).Value = "A部门验收";
        worksheet.Cell(2, 4).Value = "A部门备注";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private sealed record DepartmentFixture(
        int RootOrgUnitId,
        int CommonUserId,
        int DepartmentAId,
        int DepartmentBId);

    private sealed record DepartmentSearchFixture(
        int CustomerId,
        string Keyword,
        int[] DepartmentASpecIds,
        int DepartmentBSpecId);

    private sealed record RemarkReplaceFixture(
        string SearchText,
        string ReplacementText,
        int[] DepartmentASpecIds,
        int DepartmentBSpecId)
    {
        public int[] AllSpecIds => [.. DepartmentASpecIds, DepartmentBSpecId];
    }
}
