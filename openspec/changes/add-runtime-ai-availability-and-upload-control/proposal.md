# Change: 增加 AI 运行可用性与上传控制

## Why

业务页面目前把“配置已启用”当成“服务可调用”，且 keep-alive 页面只在首次挂载读取配置。用户从配置页返回后会看到旧状态，端点离线时也只能等待真实识别失败。共享上传区同时缺少真实进度和取消，网络上传与服务端解析使用同一静态“上传中”状态，容易被误认为卡死。

## What Changes

- 增加按 LLM/Embedding 用途查询的运行时 AI 自动选择接口，明确区分 `available`、`unavailable` 和 `checking`。
- 以短 TTL、有限并发的后台/按需探测维护运行可用性；配置变更和真实调用结果会使缓存失效或更新。
- 暂时离线只影响运行状态，不永久修改管理员控制的 `IsDisabled`。
- 数据导入和智能填充在 keep-alive 重新激活时刷新自动选择结果，无健康服务时禁用 AI 调用并提供检测/配置入口。
- 用户已开启 AI 辅助并发起结构识别时，对短暂 `checking` 状态进行有上限、可取消的等待；恢复可用后携带最新服务 ID，只有确认不可用或等待超时才降级为规则识别。
- 上传 API 封装支持 `AbortSignal` 和真实进度；共享上传区展示“上传、解析、完成、失败”独立阶段并允许取消上传。

## Impact

- Affected specs: `api`, `user-interface`
- Affected code: AI 服务选择/探测、健康状态、数据导入与智能填充 AI 控件、上传 API 封装、共享上传组件
- Compatibility: 新增只读 API 和响应字段；不删除现有 AI 列表/测试接口；不持久化瞬时健康状态
- Related changes: `fix-upload-recognition-service-selection`, `update-smart-fill-recognition-step-flow`
