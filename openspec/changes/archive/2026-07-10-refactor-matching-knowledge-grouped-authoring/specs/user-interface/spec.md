## MODIFIED Requirements
### Requirement: 运行时匹配知识不提供对外配置界面
系统 SHALL 将匹配知识限制为匹配引擎内部运行时知识，不再提供 matching-knowledge 配置页、分组式作者视图或草稿生成入口。

#### Scenario: 客户端访问旧配置页
- **WHEN** 用户尝试访问旧的 matching-knowledge 配置页
- **THEN** 系统不再提供实体组、单位组、字段组、左右冲突组等作者界面
- **AND** 系统不再展示任何 matching-knowledge 维护入口

#### Scenario: 页面不再回显旧作者模型
- **WHEN** 前端加载系统配置导航与页面清单
- **THEN** 不再包含 matching-knowledge 页面、草稿弹窗或相关权限入口
- **AND** 不再暴露与 matching-knowledge 作者模型相关的旧提示文案
