# Implementation Plan: 验收规范智能识别系统

## Overview

基于React前端 + C#后端的验收规范智能识别系统实现计划。采用Embedding召回 + LLM精排的架构，所有数据使用JSON文件存储。

## Tasks

- [x] 1. 项目初始化与基础架构
  - [x] 1.1 创建C# .NET 8 Web API项目结构
    - 创建解决方案和项目文件
    - 配置依赖注入和中间件
    - 设置CORS支持React前端
    - _Requirements: 6.1_
  - [x] 1.2 创建React + TypeScript前端项目
    - 使用Vite创建React项目
    - 配置TypeScript和路由
    - 设置API客户端
    - _Requirements: 6.1_
  - [x] 1.3 创建数据模型和接口定义
    - 定义所有C#数据模型类
    - 定义TypeScript类型定义
    - _Requirements: 5.1, 7.1, 7.2, 7.3_

- [x] 2. 数据存储层实现
  - [x] 2.1 实现JSON文件存储服务
    - 实现通用的JSON读写服务
    - 支持文件监视和热加载
    - _Requirements: 5.1, 7.7_
  - [x] 2.2 创建预置数据文件
    - 创建历史记录示例数据 (history_records.json)
    - 创建同义词库数据 (synonyms.json)
    - 创建关键字库数据 (keywords.json)
    - 创建错别字映射数据 (typo_corrections.json)
    - 创建单位映射数据 (unit_mappings.json)
    - 创建系统配置文件 (config.json)
    - _Requirements: 5.6, 7.4, 7.5, 7.6_
  - [x] 2.3 编写数据存储层单元测试
    - 测试JSON读写正确性
    - 测试热加载功能
    - _Requirements: 5.1_

- [x] 3. 文本预处理模块
  - [x] 3.1 实现TextPreprocessor服务
    - 实现符号标准化（中文标点→英文标点，全角→半角）
    - 实现空白字符标准化
    - _Requirements: 1.1, 1.2, 1.3_
  - [x] 3.2 实现错别字修正功能
    - 加载错别字映射表
    - 实现文本扫描和替换
    - _Requirements: 1.4_
  - [x] 3.3 实现单位标准化功能
    - 解析数值和单位
    - 标准化单位表述（伏→V，千瓦→KW）
    - 保留电气前缀（DC/AC）作为独立属性
    - _Requirements: 11.1, 11.2, 11.5_
  - [x] 3.4 编写文本预处理属性测试
    - **Property 1: 文本预处理保持语义完整性**
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4**
  - [x] 3.5 编写单位标准化属性测试
    - **Property 10: 单位标准化正确性**
    - **Validates: Requirements 11.1, 11.5**

- [x] 4. Embedding服务模块
  - [x] 4.1 实现EmbeddingService接口
    - 集成OpenAI Embedding API
    - 实现文本向量生成
    - 实现批量向量生成
    - _Requirements: 2.1, 2.4_
  - [x] 4.2 实现余弦相似度计算
    - 实现向量相似度计算函数
    - _Requirements: 2.2_
  - [x] 4.3 实现向量缓存服务
    - 实现向量索引构建
    - 实现增量更新
    - _Requirements: 2.5_
  - [x] 4.4 编写Embedding服务属性测试
    - **Property 2: 向量生成一致性**
    - **Property 3: 余弦相似度范围正确性**
    - **Validates: Requirements 2.1, 2.2**

- [x] 5. Checkpoint - 确保基础模块测试通过
  - 运行所有单元测试和属性测试
  - 确认预处理和Embedding模块正常工作
  - 如有问题请询问用户

- [x] 6. 同义词扩展模块
  - [x] 6.1 实现SynonymExpander服务
    - 加载同义词库
    - 实现查询文本同义词扩展
    - _Requirements: 8.1, 8.3_
  - [x] 6.2 编写同义词扩展单元测试
    - 测试同义词查找
    - 测试双向/单向同义关系
    - _Requirements: 8.2_

- [x] 7. 关键字高亮模块
  - [x] 7.1 实现KeywordHighlighter服务
    - 加载关键字库
    - 实现关键字识别和高亮
    - 支持同义词高亮
    - _Requirements: 4.1, 4.2_
  - [x] 7.2 编写关键字高亮属性测试
    - **Property 7: 关键字高亮完整性**
    - **Validates: Requirements 4.1, 4.2**

- [x] 8. LLM服务模块
  - [x] 8.1 实现LLMService接口
    - 集成OpenAI Chat API
    - 实现候选结果分析
    - 实现冲突检测（DC/AC、单相/三相等）
    - _Requirements: 2.6, 2.7, 11.6_
  - [x] 8.2 实现LLM降级策略
    - 实现超时和重试机制
    - 实现降级到纯Embedding模式
    - _Requirements: 3.1, 3.2, 3.3_
  - [x] 8.3 编写LLM服务单元测试
    - 测试Prompt构建
    - 测试响应解析
    - 测试冲突检测（DC/AC、单相/三相、NPN/PNP）
    - _Requirements: 2.6_

- [x] 9. 匹配引擎核心模块
  - [x] 9.1 实现MatchingEngine服务
    - 整合预处理、Embedding、同义词扩展
    - 实现Top-N候选召回
    - 整合LLM分析
    - _Requirements: 2.3_
  - [x] 9.2 实现置信度评估
    - 实现三级置信度分级（高/中/低/无匹配）
    - 实现多候选相似度接近检测
    - _Requirements: 3.1, 3.2, 3.3, 10.2_
  - [x] 9.3 实现匹配结果解释生成
    - 生成匹配解释信息
    - 记录预处理步骤
    - _Requirements: 8.5_
  - [x] 9.4 编写匹配引擎属性测试
    - **Property 4: Top-N结果排序正确性**
    - **Property 5: 增量索引一致性**
    - **Property 6: 置信度分级正确性**
    - **Property 8: 匹配结果完整性**
    - **Validates: Requirements 2.3, 2.5, 3.1, 3.2, 3.3, 8.1, 8.2, 8.3, 8.5**

- [x] 10. Checkpoint - 确保核心匹配功能测试通过
  - 运行所有属性测试
  - 验证端到端匹配流程
  - 如有问题请询问用户

- [x] 11. 批量处理模块
  - [x] 11.1 实现BatchProcessor服务
    - 实现批量匹配处理
    - 实现进度跟踪
    - 实现任务取消
    - _Requirements: 9.1, 9.2, 9.3_
  - [x] 11.2 编写批量处理属性测试
    - **Property 9: 批量处理结果一致性**
    - **Validates: Requirements 9.2**

- [x] 12. 审计日志模块
  - [x] 12.1 实现AuditLogger服务
    - 实现查询日志记录
    - 实现用户操作日志记录
    - 实现配置修改日志记录
    - _Requirements: 12.1, 12.2, 12.3_
  - [x] 12.2 实现日志查询功能
    - 支持时间范围筛选
    - 支持操作类型筛选
    - _Requirements: 12.4_
  - [x] 12.3 编写审计日志单元测试
    - 测试日志记录
    - 测试日志查询
    - _Requirements: 12.1_

- [x] 13. 配置管理模块
  - [x] 13.1 实现ConfigManager服务
    - 实现配置读取和更新
    - 实现配置修改历史记录
    - 实现参数验证
    - _Requirements: 6.1, 6.2, 6.3, 6.5_
  - [x] 13.2 编写配置管理单元测试
    - 测试配置读写
    - 测试参数验证
    - _Requirements: 6.5_


- [x] 14. 后端API层实现
  - [x] 14.1 实现MatchController
    - POST /api/match - 单条匹配
    - POST /api/match/batch - 批量匹配
    - POST /api/match/confirm - 确认匹配结果
    - _Requirements: 8.1, 8.2, 9.1_
  - [x] 14.2 实现HistoryController
    - GET /api/history - 获取历史记录
    - POST /api/history - 添加历史记录
    - PUT /api/history/{id} - 更新历史记录
    - _Requirements: 5.2, 5.4, 5.5_
  - [x] 14.3 实现ConfigController
    - GET /api/config - 获取配置
    - PUT /api/config - 更新配置
    - GET /api/config/synonyms - 获取同义词库
    - GET /api/config/keywords - 获取关键字库
    - _Requirements: 6.1, 6.2, 8.4_
  - [x] 14.4 实现AuditController
    - GET /api/audit - 查询审计日志
    - _Requirements: 12.4_
  - [x] 14.5 编写API集成测试
    - 测试各API端点
    - 测试错误处理
    - _Requirements: 8.1_

- [x] 15. Checkpoint - 确保后端API测试通过
  - 运行所有API测试
  - 使用Postman或类似工具手动验证
  - 如有问题请询问用户

- [x] 16. React前端基础组件
  - [x] 16.1 实现API客户端服务
    - 封装所有后端API调用
    - 实现错误处理
    - _Requirements: 8.1_
  - [x] 16.2 实现通用UI组件
    - ConfidenceBadge - 置信度徽章
    - HighlightedText - 高亮文本组件
    - LoadingSpinner - 加载指示器
    - _Requirements: 8.3, 8.4_
  - [x] 16.3 实现MatchResultCard组件
    - 显示匹配结果
    - 显示高亮关键字
    - 显示置信度
    - 确认/拒绝按钮
    - _Requirements: 8.1, 8.2, 8.3, 8.4_
  - [x] 16.4 编写前端组件测试
    - 测试组件渲染
    - 测试交互行为
    - _Requirements: 8.1_

- [x] 17. React前端页面实现
  - [x] 17.1 实现匹配查询页面 (MatchPage)
    - 查询输入表单
    - 匹配结果展示
    - 确认/修改操作
    - _Requirements: 8.1, 8.2, 3.4_
  - [x] 17.2 实现批量处理页面 (BatchPage)
    - 文件上传组件
    - 进度显示
    - 结果汇总展示
    - 导出功能
    - _Requirements: 9.1, 9.2, 9.3, 9.4_
  - [x] 17.3 实现历史记录管理页面 (HistoryPage)
    - 历史记录列表
    - 搜索和筛选
    - 编辑功能
    - _Requirements: 5.4, 5.5_
  - [x] 17.4 实现配置管理页面 (ConfigPage)
    - 系统配置编辑
    - 同义词库管理
    - 关键字库管理
    - 错别字映射管理
    - _Requirements: 6.1, 6.2, 4.3, 4.4, 8.2, 8.4_
  - [x] 17.5 实现审计日志页面 (AuditPage)
    - 日志列表展示
    - 时间范围筛选
    - 操作类型筛选
    - _Requirements: 12.4_
  - [x] 17.6 编写页面集成测试
    - 测试页面渲染
    - 测试用户流程
    - _Requirements: 8.1_

- [x] 18. 前端路由和布局
  - [x] 18.1 实现应用布局和导航
    - 侧边栏导航
    - 页面路由配置
    - _Requirements: 6.1_
  - [x] 18.2 实现响应式设计
    - 适配不同屏幕尺寸
    - _Requirements: 6.1_

- [x] 19. Checkpoint - 确保前端功能测试通过
  - 运行所有前端测试
  - 手动测试各页面功能
  - 如有问题请询问用户

- [x] 20. 系统集成与优化
  - [x] 20.1 实现健康检查服务
    - 检查Embedding API连接
    - 检查LLM API连接
    - 检查数据文件状态
    - _Requirements: 6.1_
  - [x] 20.2 实现用户反馈学习机制
    - 记录用户反馈
    - 生成同义词建议
    - _Requirements: 3.4, 8.6_
  - [x] 20.3 编写端到端集成测试
    - 测试完整匹配流程
    - 测试批量处理流程
    - _Requirements: 8.1, 9.1_

- [x] 21. Final Checkpoint - 确保所有测试通过
  - 运行所有单元测试、属性测试、集成测试
  - 验证99%准确率目标的实现策略
  - 确认所有需求已覆盖
  - 如有问题请询问用户

## Notes

- 所有任务均为必需，确保全面测试覆盖
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties
- Unit tests validate specific examples and edge cases
- 属性测试使用FsCheck库（C#）
- 前端测试使用Jest + React Testing Library
