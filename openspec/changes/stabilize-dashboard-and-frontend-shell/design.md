## Context

当前首页用七个 summary 请求拼接趋势，导致数据库和浏览器产生瀑布式并发；请求没有 owner version，迟到响应还会反写周期。前端构建通过提高 chunk 告警阈值隐藏体积问题，导航组件又缺少原生交互语义。

## Goals / Non-Goals

### Goals

- 仪表盘以固定少量请求返回一致快照。
- 所有页面状态只接受当前请求响应。
- 为首屏和异步块建立可阻断的体积预算。
- 键盘用户能完整操作顶部导航、搜索和标签页。

### Non-Goals

- 不重做品牌、颜色或整体布局。
- 不更换 Vue、Element Plus 或路由框架。
- 不以提高告警阈值代替体积治理。

## Decisions

### Decision 1: 汇总接口一次返回连续趋势

扩展现有 dashboard summary 响应，增加 `dailyTrend`。后端按当前用户数据范围和日期分组一次查询，并对所选周期缺失日期补零。最近执行继续使用既有 execution-history API，因此首屏只请求 summary 和 recent executions。

### Decision 2: 页面以 AbortController 和 requestVersion 管理新鲜度

周期切换取消旧 summary 并递增版本，只有版本和周期仍一致的响应才能写入。周期切换不重复请求 recent；手动刷新才并发刷新 summary 与 recent，各一次。取消不显示为错误。

### Decision 3: 小型趋势图使用原生 SVG

七点趋势不需要通用图表运行时。Sparkline 使用带无障碍名称的 SVG polyline/path，处理空数据、单点和全零数据；移除不再使用的 ECharts 依赖和插件。Element Plus 只注册仓库实际使用组件，并用扫描测试防止删漏。

### Decision 4: 包体积使用可执行预算

恢复合理 chunk 告警，并增加构建后预算脚本。初始预算为主入口 gzip 不超过 500KB、Dashboard 异步块 gzip 不超过 100KB；实施后若真实基线更低则记录并收紧。超预算返回非零退出码并在 CI 阻断。

### Decision 5: 原生语义优先

设置、全屏、滚动、关闭等入口使用 `<button type="button">`；Logo 使用链接；搜索结果使用可选择的按钮/option；标签使用 tablist/tab、`aria-selected` 和 roving tabindex，支持左右键、Home、End、Enter/Space 和 Delete。图标按钮提供 aria-label、可见焦点和至少 44×44px 触达区。

## Risks / Trade-offs

- SVG Sparkline 功能少于 ECharts，但当前只需要小型静态趋势，收益是显著降低包体积。
- Element Plus 显式注册表需要守卫防止新增页面使用未注册组件。
- 初始包体积预算必须以本变更完成后的可重复构建为基线，不能随意放宽规避失败。

## Rollback Plan

新增 `dailyTrend` 不影响旧客户端。前端可按仪表盘、包体积、无障碍三个主题提交独立回退，但包体积预算和语义回归测试不得在没有替代方案时静默删除。
