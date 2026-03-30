# Change: 登录账号从配置文件迁移到数据库

## Why
当前登录账号依赖 `appsettings` 配置，账号生命周期和权限审计能力不足，不符合长期运维和安全要求。  
需要将账号与权限信息纳入数据库统一管理。

## What Changes
- 新增 `SystemUsers` 表，存储用户名、密码哈希、角色与权限。
- 登录与刷新令牌流程改为数据库查询用户，不再读取 `AuthUsers` 配置。
- 系统启动时在用户表为空的情况下写入默认账号（仅初始化一次）。
- 新增系统用户管理 API（列表、详情、新增、编辑、改密、启停、删除）。
- 新增系统用户管理前端页面并接入配置菜单。
- 增加未登录 401、非管理员 403、非法刷新令牌 401 的鉴权测试覆盖。
- 修复前端 AI 服务配置页面的类型错误，确保 `typecheck` 通过。

## Impact
- Affected specs: `api`, `data-storage`
- Affected code:
  - `src/AcceptanceSpecSystem.Data/*`（实体、仓储、迁移）
  - `src/AcceptanceSpecSystem.Api/*`（AuthController、Program、密码服务）
  - `tests/AcceptanceSpecSystem.Api.Tests/*`（鉴权测试）
  - `web/src/views/config/ai-services/index.vue`（前端类型修复）
