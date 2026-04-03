## 1. Implementation
- [x] 1.1 扩展匹配配置模型，新增 LLM 实体判别开关、TopM 与阈值参数。
- [x] 1.2 新增运行时实体候选提取与轻量归一化组件，支持英文品牌、中文品牌和常见组织后缀清洗。
- [x] 1.3 新增 LLM 实体判别服务与固定 JSON 协议，关系枚举至少覆盖 `same`、`alias_same`、`conflict`、`unknown`。
- [x] 1.4 在多阶段匹配重排中集成实体判别阶段，并确保数值/型号等硬规则优先。
- [x] 1.5 将实体判别结果映射为 `EntityEvidence` 和结构化 `issues`，对未知项做保守降级。
- [x] 1.6 在 API DTO 中透传新增配置与实体问题说明。
- [x] 1.7 在智能填充配置界面中新增 LLM 实体判别开关与阈值输入。
- [x] 1.8 复用现有预览/详情问题展示，确保实体同一、冲突、未知三类文案可见。

## 2. Verification
- [x] 2.1 补充 Core 单元测试，覆盖无配置别名识别、实体冲突、未知实体和低置信降级。
- [x] 2.2 补充 API 集成测试，验证配置透传和实体问题项返回。
- [x] 2.3 运行 `dotnet test` 相关测试集与 `pnpm build`。
- [x] 2.4 运行 `openspec validate add-llm-entity-resolution --strict`。
