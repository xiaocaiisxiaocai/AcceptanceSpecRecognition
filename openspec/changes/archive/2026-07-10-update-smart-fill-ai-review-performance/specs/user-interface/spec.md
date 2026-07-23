## MODIFIED Requirements
### Requirement: 智能填充界面
The system SHALL provide a smart-fill interface that supports match parameter configuration, preview, manual review, optional AI review, execution, and result download.

#### Scenario: 匹配参数配置
- **WHEN** the user configures smart-fill matching
- **THEN** the interface SHALL allow selecting matching scope, Embedding service, LLM service, thresholds, recall count, ambiguity margin, exact-match mode, empty-row filtering, and whether to enable synchronous AI equivalence adjudication

#### Scenario: 匹配预览
- **WHEN** the user starts preview
- **THEN** the interface SHALL show matched rows, confidence levels, no-match reasons, and decision details

#### Scenario: 详情弹窗
- **WHEN** the user opens a row detail
- **THEN** the interface SHALL show source text, selected candidate, top candidates, score details, evidence, conflicts, and AI equivalence result when present

#### Scenario: LLM复核
- **WHEN** the user triggers LLM review for rows requiring confirmation
- **THEN** the interface SHALL stream review progress and update row review state without blocking initial preview

#### Scenario: 执行填充
- **WHEN** the user confirms mappings and executes fill
- **THEN** the interface SHALL submit fill mappings and provide the generated result download
