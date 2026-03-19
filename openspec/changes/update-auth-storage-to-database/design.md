## Context
当前登录账号来源于 `appsettings`，不利于权限运维和审计。系统已具备 EF Core 迁移能力，适合将用户体系纳入数据库管理。

## Goals / Non-Goals
- Goals:
  - 账号从配置迁移到数据库
  - 登录仅支持哈希验密（PBKDF2）
  - 保持现有 JWT 与前端协议不变
- Non-Goals:
  - 暂不引入完整 RBAC 表模型（角色/权限先以 JSON 字段存储）
  - 暂不提供用户管理页面

## Decisions
- Decision: 新增 `SystemUsers` 表，字段包含 `Username`、`PasswordHash`、`RolesJson`、`PermissionsJson`、`IsActive` 等。  
  Alternatives considered: 继续使用配置文件；否决原因是无法满足运维与安全诉求。
- Decision: 使用 PBKDF2-SHA256 进行密码哈希，并在服务端固定时间比较。  
  Alternatives considered: BCrypt；当前选择 PBKDF2 以减少依赖并满足内网部署场景。
- Decision: 默认账号在表为空时自动初始化一次。  
  Alternatives considered: 强制手工初始化；会提高首次部署复杂度。
- Decision: 提供 `admin` 保护的系统用户管理 API，覆盖新增、修改、改密、启停、删除。  
  Alternatives considered: 仅保留手工改库；否决原因是运维成本高且易误操作。

## Risks / Trade-offs
- 风险: 默认账号密码未及时修改存在安全风险。  
  Mitigation: 启动日志明确提示尽快改密，部署文档强调首次改密。
- 风险: `RolesJson`/`PermissionsJson` 查询与维护不如关系表直观。  
  Mitigation: 当前仅用于登录发 token，后续可平滑迁移到关系模型。

## Migration Plan
1. 应用 EF 迁移创建 `SystemUsers` 表。  
2. 启动时若表为空，自动写入默认账号。  
3. 登录接口切换为读取数据库账号。  
4. 下线并移除 `AuthUsers` 配置项。  

## Open Questions
- 是否需要在下一阶段提供后台“账号管理”页面与改密接口。
