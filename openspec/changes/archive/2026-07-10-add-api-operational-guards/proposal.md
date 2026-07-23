# Change: add API operational guards

## Why
当前 API 缺少可配置限流和真实健康检查，生产环境遇到登录爆破、上传滥用、AI 调用挤占或基础设施异常时，缺少明确的保护与探活信号。

## What Changes
- 为登录、文件上传、AI/匹配重接口增加可配置限流策略。
- 将 `/health` 从固定 healthy 响应升级为数据库与文件存储可用性检查。
- 保持默认配置适合内网部署，避免对普通页面查询接口做全局强限流。

## Impact
- Affected specs: api, architecture
- Affected code: `Program.cs`、关键控制器限流标记、健康检查实现、API 集成测试、部署配置示例
