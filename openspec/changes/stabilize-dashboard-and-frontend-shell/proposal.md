# Change: 稳定仪表盘与前端壳层

## Why

仪表盘首屏为七天趋势逐日发起请求，快速切换周期时旧响应可覆盖新结果；一个小型趋势图却引入大型 ECharts 异步块。核心导航同时依赖可点击 `div/span`，键盘用户无法完整完成搜索、切换标签、关闭页面和打开设置。

## What Changes

- 扩展仪表盘汇总 API，一次返回所选周期摘要和连续每日趋势，首屏请求从约九个收敛为两个。
- 为仪表盘异步请求增加取消和版本校验，周期切换只刷新汇总，手动刷新才同步刷新最近执行。
- 使用轻量原生 SVG 实现小型趋势图，收敛 Element Plus 注册并建立可执行的 gzip 包体积预算。
- 将核心导航交互改为语义按钮、链接、tablist/option，并支持键盘、可见焦点和无障碍名称。
- 用组件行为测试和 Playwright 键盘路径代替源码字符串断言作为主要验收。

## Impact

- Affected specs: `api`, `user-interface`
- Affected code: Dashboard API/查询、仪表盘页面、Sparkline、Element Plus/ECharts 注册、Vite 构建预算、顶部导航/标签/搜索及测试
- Compatibility: 汇总 API 仅增加趋势字段；现有摘要字段和路由保持不变
