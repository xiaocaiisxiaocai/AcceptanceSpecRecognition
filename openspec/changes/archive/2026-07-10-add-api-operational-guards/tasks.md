## 1. Implementation
- [x] 1.1 添加限流与健康检查集成测试。
- [x] 1.2 在 API 启动流程注册命名限流策略。
- [x] 1.3 给登录、上传、AI/匹配重接口应用对应限流策略。
- [x] 1.4 将 `/health` 接入数据库与文件存储检查，并保持匿名访问。
- [x] 1.5 补充配置示例与 CI 验证。
- [x] 1.6 运行 `dotnet test AcceptanceSpecSystem.sln --no-restore -m:1`。
