using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Tests.Infrastructure;

/// <summary>
/// 集成测试的业务归属部门辅助。
/// 启动种子会创建默认业务部门，管理员上传文档、新增规格时必须显式指定 businessOrgUnitId；
/// 这里按前端相同的方式调用 business-context 接口取得当前身份可用的业务部门，
/// 让测试显式传参，而不是放宽生产侧的部门隔离校验。
/// </summary>
public static class TestBusinessOrg
{
    /// <summary>
    /// 上传与新增规格接口使用的业务归属字段名。
    /// </summary>
    public const string FieldName = "businessOrgUnitId";

    /// <summary>
    /// 取得当前客户端身份下的默认业务部门 Id：
    /// 需要选择时取第一个可选业务部门；无需选择（普通用户或无业务部门）时取当前归属。
    /// </summary>
    public static async Task<int> GetDefaultBusinessOrgUnitIdAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/org-units/business-context");
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"获取业务归属上下文失败：{(int)response.StatusCode} {body}");
        }

        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        if (data.GetProperty("requiresSelection").GetBoolean())
        {
            var options = data.GetProperty("options");
            if (options.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("业务归属上下文要求选择部门，但没有可选部门");
            }

            return options[0].GetProperty("id").GetInt32();
        }

        return data.GetProperty("currentOrgUnitId").GetInt32();
    }

    /// <summary>
    /// 直接写库造数时使用的默认业务部门 Id，选取规则与 business-context 接口一致
    /// （同公司下首个激活的非公司节点，按层级、排序、Id）。
    /// 造出的规格必须与导入/填充所在的业务部门一致，否则会被部门隔离过滤掉。
    /// </summary>
    public static async Task<int> GetDefaultBusinessOrgUnitIdAsync(AppDbContext dbContext, int companyId = 1)
    {
        return await dbContext.OrgUnits
            .AsNoTracking()
            .Where(org => org.CompanyId == companyId && org.IsActive && org.UnitType != OrgUnitType.Company)
            .OrderBy(org => org.Depth)
            .ThenBy(org => org.Sort)
            .ThenBy(org => org.Id)
            .Select(org => org.Id)
            .FirstAsync();
    }

    /// <summary>
    /// 为上传表单追加当前身份的默认业务归属部门字段。
    /// </summary>
    public static async Task AddDefaultBusinessOrgUnitIdAsync(
        this MultipartFormDataContent content,
        HttpClient client)
    {
        var orgUnitId = await GetDefaultBusinessOrgUnitIdAsync(client);
        content.Add(new StringContent(orgUnitId.ToString()), FieldName);
    }
}
