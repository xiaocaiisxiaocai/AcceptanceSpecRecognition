## Context

性能瓶颈并非单一渲染组件，而是“无界读取 + 全量响应 + 前端 O(行×列) 扫描 + 双表 DOM”叠加。仅替换为虚拟表格仍会保留大响应和响应式内存，因此先在 API 边界建立有界窗口。

## Goals / Non-Goals

### Goals

- 大工作表首次只传输和渲染有限窗口。
- 快速切换文件、Sheet 或页面时不显示过期响应。
- 保持完整对比统计和导出能力。

### Non-Goals

- 不改变差异判定算法或导出文件格式。
- 不新增前端虚拟列表依赖；有界窗口已满足首轮风险收敛。
- 不把分页预览伪装成已经加载全部文档。

## Decisions

### Decision 1: 扩展现有表预览契约

现有预览接口增加可选 `rowOffset`、`previewRows`、`columnOffset`、`previewColumns`，响应增加对应 offset 和 `totalRows/totalColumns`。新文件对比页面默认请求 200×60，服务端分别限制最大 500 行和 100 列。负偏移或无效窗口返回 400；尾页返回实际剩余数据。

### Decision 2: 预览与导出分离

文件对比请求增加 `includeUnchanged` 可选字段，新页面传 `false`，响应中的差异 items 只包含 Added/Removed/Modified，但四类统计仍覆盖完整文档。完整导出在服务端基于全量对比结果生成，不依赖前端已加载窗口。

### Decision 3: 使用窗口分页而不是前端全表虚拟化

`CompareTableGrid` 仅接收当前窗口并渲染普通语义表格，展示行列范围和总量。上一页、下一页、指定范围和差异跳转更新同一窗口；左右表始终使用相同 offset 和 size。

### Decision 4: 取消和版本校验同时存在

API 封装接收 `AbortSignal`。页面切换文件、Sheet、重新对比或 deactivated 时取消旧请求，同时递增 `requestVersion`；只有版本、文件 ID 和 Sheet 索引仍一致的响应才能写入状态。取消不显示失败提示，真实错误提供重试。

## Risks / Trade-offs

- 窗口分页不能像完整 DOM 一样浏览器内全文搜索，页面应提供范围与差异跳转。
- 旧客户端的无窗口行为暂时兼容，但新页面和新测试禁止传 `previewRows=0`；后续是否废弃无界语义需另立变更。

## Rollback Plan

新字段均为可选，前端可按主题提交回退；服务端保留旧参数解释，因此回退页面不会导致 API 不兼容。
