## MODIFIED Requirements
### Requirement: AI 等价裁决门禁
The system SHALL support AI equivalence adjudication as an optional smart-fill gate, and SHALL NOT run it during synchronous matching unless the matching configuration explicitly enables it.

#### Scenario: 当前最佳候选进入 AI 等价裁决
- **WHEN** smart-fill matching configuration enables AI equivalence adjudication and the current best candidate reaches the configured adjudication gate
- **THEN** the system SHALL request an AI equivalence verdict for the current best candidate

#### Scenario: 等价裁决未通过时回退人工确认
- **WHEN** AI equivalence adjudication returns `different`, `uncertain`, no valid result, or fails
- **THEN** the system SHALL require manual confirmation for the row

#### Scenario: 默认跳过同步等价裁决
- **WHEN** smart-fill matching configuration does not explicitly enable AI equivalence adjudication
- **THEN** the system SHALL skip AI equivalence adjudication during synchronous matching and continue with local evidence-based review state
