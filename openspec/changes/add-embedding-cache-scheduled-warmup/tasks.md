## 1. Data Model
- [x] 1.1 为 `EmbeddingCache` 增加 `Usage` 与 `TextHash` 字段
- [x] 1.2 调整 `AppDbContext` 索引与字段约束
- [x] 1.3 扩展 `IEmbeddingCacheRepository` / `EmbeddingCacheRepository` 查询接口
- [x] 1.4 添加 EF Core Migration
- [x] 1.5 补充 Data 层迁移与仓储测试

## 2. Cache Service
- [x] 2.1 新增统一的缓存用途枚举与文本构造逻辑
- [x] 2.2 新增 `EmbeddingCacheWarmupOptions`
- [x] 2.3 新增可复用的规格向量缓存服务，支持按用途查找、生成、写入、失效
- [x] 2.4 将智能填充预览、匹配执行、语义搜索改为复用统一服务
- [x] 2.5 导入重复检测改为复用持久化缓存

## 3. Scheduled Warmup
- [x] 3.1 新增 `EmbeddingCacheWarmupService : BackgroundService`
- [x] 3.2 支持每日本地时间、批大小、单轮最大数量、启动是否执行等配置
- [x] 3.3 在 `Program.cs` 注册配置与 HostedService
- [x] 3.4 在 `appsettings.json` 增加显式配置段
- [x] 3.5 后台任务失败只记录日志，不阻断应用启动

## 4. Cache Invalidation
- [x] 4.1 `AcceptanceSpecAppService.UpdateAsync` 修改规格后清理相关缓存
- [x] 4.2 `DocumentImportAppService` 覆盖已有规格后清理相关缓存
- [x] 4.3 保留删除规格时由级联删除缓存
- [x] 4.4 `SmartFillSpecBackfillAppService` 继续清理相关缓存

## 5. Tests
- [x] 5.1 新增后台预热服务测试：关闭、补齐、分批、失败吞吐
- [x] 5.2 新增缓存用途隔离测试：匹配与语义搜索不互相复用
- [x] 5.3 新增规格更新缓存失效测试
- [ ] 5.4 运行 `dotnet test AcceptanceSpecSystem.sln -c Debug`
- [x] 5.5 必要时运行 `pnpm build`（仅当前端未改动时可跳过并说明）

## 6. Management UI
- [x] 6.1 新增预热配置/状态/手动触发 API
- [x] 6.2 新增前端 API 封装
- [x] 6.3 在配置管理菜单增加 Embedding 预热页面
- [x] 6.4 实现配置表单、状态摘要和立即预热操作
- [x] 6.5 运行前端类型检查或构建
