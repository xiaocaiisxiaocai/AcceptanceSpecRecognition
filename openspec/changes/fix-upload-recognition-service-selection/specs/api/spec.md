## ADDED Requirements

### Requirement: 智能结构识别显式声明 LLM 辅助
系统 SHALL 允许智能结构识别请求显式声明是否启用 LLM 辅助及唯一的 LLM 服务标识；未声明或关闭辅助时，服务端 MUST 使用零 LLM 调用完成规则、模板和确定性识别。

#### Scenario: 旧请求默认不调用 LLM
- **GIVEN** 客户端请求只包含文件和客户标识
- **WHEN** 客户端调用 `POST /api/smart-config/recognize`
- **THEN** 服务端执行规则、模板和确定性识别
- **AND** 服务端不得调用任意 LLM 服务

#### Scenario: 开启辅助时只使用所选服务
- **GIVEN** 客户端开启 LLM 辅助并提交一个已启用的 LLM 服务标识
- **WHEN** 客户端调用智能结构识别
- **THEN** 服务端只允许调用该标识对应的服务
- **AND** 不得遍历其他已启用 LLM 作为回退

#### Scenario: 所选服务不可用时保留规则结果
- **GIVEN** 客户端所选服务已禁用、用途错误或调用失败
- **WHEN** 服务端执行智能结构识别
- **THEN** 接口仍返回可用的规则识别结果
- **AND** 返回可解释的 LLM 辅助未执行或失败问题
- **AND** 不得改用未选择的服务
