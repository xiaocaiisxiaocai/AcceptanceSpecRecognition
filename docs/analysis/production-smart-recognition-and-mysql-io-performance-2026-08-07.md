# 生产环境智能识别与 MySQL I/O 性能分析

**分析日期**：2026-08-07  
**分析对象**：Windows Docker Desktop 生产环境  
**部署版本**：`6e84374c8570ef8a5d83c0ee51a063043231c4a5`  
**分析性质**：只读诊断记录，本次未修改应用代码、Docker 配置或生产数据

## 一、结论摘要

本次用户流程中存在两个相互独立的性能瓶颈：

1. **智能结构识别约 20 秒**：主要耗时来自 `qwen2.5:14b` 的单次长提示词推理。两次样本中，AI 辅助分别耗时 `19.311s` 和 `19.361s`，占识别总耗时约 `91.6%` 和 `95.3%`。这不是模型冷加载、Excel 解析或数据库慢查询造成的。
2. **Excel 导入偶发增加约 6.2 秒**：已定位到每次幂等导入前同步执行的过期记录清理：

   ```sql
   DELETE FROM DocumentImportExecutions
   WHERE ExpiresAt <= ?;
   ```

   该语句三次调用合计只检查 `4` 行、影响 `3` 行，其中一次耗时 `6.237s`。`ExpiresAt` 索引已经存在，且 MySQL 没有行锁等待；真正的高延迟来自 Docker 数据虚拟磁盘所在机械硬盘的多秒级文件 I/O 等待。

生产服务当前运行正常，不需要因本次性能诊断回退部署。后续优化应同时处理应用请求内清理和基础设施存储，但两者都不会直接降低 LLM 的约 19 秒推理时间。

## 二、诊断范围与边界

### 已检查

- API、Web、MySQL 容器健康状态。
- Web 首页及 Web 代理后的 API readiness。
- Ollama LLM 与 Embedding 实际 HTTP 调用日志。
- 智能结构识别、文档上传、表格读取和规则加载阶段耗时。
- EF Core 慢查询日志及 MySQL Performance Schema 语句摘要。
- MySQL 行锁、日志等待、文件 I/O 等待和持久化配置。
- Docker MySQL 数据卷、容器资源、虚拟磁盘路径和宿主物理磁盘类型。

### 未检查或不能由现有证据证明

- Performance Schema 文件等待是 MySQL 启动以来的聚合值，不包含每次文件等待与业务请求的一对一时间戳关联。
- 尚未采集 Windows 物理磁盘在问题发生瞬间的队列长度、响应时间和安全软件活动。
- 尚未使用固定输入集对不同 LLM 模型、量化版本、上下文长度或提示词方案做质量与延迟对比。
- 尚未实施或验证任何性能优化。

因此，“数据库高延迟来自 Docker 数据盘 I/O”属于高置信度结论；“具体是哪一次物理写入、后台任务或宿主进程触发了该次 I/O 尖峰”仍需更细粒度的关联指标才能确定。

## 三、运行状态基线

部署完成后的运行证据：

| 项目 | 结果 |
|---|---:|
| API 容器 | `healthy` |
| Web 容器 | `healthy` |
| MySQL 容器 | `healthy` |
| API readiness | `Healthy` |
| Database / FileStorage / Migrations / SingleCompany | 全部 `Healthy` |
| 待执行破坏性迁移 | `0` |
| Web 首页 | HTTP `200` |
| Web 代理 API readiness | `Healthy` |
| LLM / Embedding Ollama 请求 | 均返回 HTTP `200` |
| LLM 与 Embedding 模型 | 均为 GPU 常驻 |

本次诊断的是性能问题，不是服务不可用、迁移失败或数据异常。

## 四、智能结构识别耗时

### 4.1 两次识别样本

| 阶段 | 文件 582 | 文件 583 |
|---|---:|---:|
| 临时上传 | `652ms` | `378ms` |
| 表格元数据读取 | `641ms` | `79ms` |
| 识别解析 | `317ms` | `83ms` |
| 规则加载 | `13ms` | `2ms` |
| AI 辅助 | `19,311ms` | `19,361ms` |
| 识别请求总耗时 | `21,085ms` | `20,324ms` |
| AI 辅助占比 | 约 `91.6%` | 约 `95.3%` |
| AI 辅助结果 | `applied` | `applied` |
| AI 调用次数 | `1` | `1` |

### 4.2 Ollama 调用分解

两次长 Chat 调用分别约为：

- `19.253s`
- `19.358s`

其中模型加载只有约 `48ms` 至 `52ms`，eval 阶段约 `8.17s` 至 `8.19s`。其余时间主要在提示词处理、上下文计算和响应生成链路中。日志中没有重试、超时、HTTP `499`、HTTP `5xx` 或 CUDA OOM 证据。

Embedding 表现正常：

| 输入数量 | 耗时 |
|---:|---:|
| 100 条 | `1,534ms` |
| 16 条 | `266ms` |
| 5 条 | `113ms` |
| 单条探测 | 多数约 `50ms` 至 `100ms` |

### 4.3 判断

- 模型已常驻 GPU，因此不是冷启动。
- Excel 解析和规则加载合计远低于 LLM 调用。
- 当前约 20 秒的识别等待主要由一次 `qwen2.5:14b` 长提示词推理决定。
- 优化数据库存储不会显著缩短这一阶段。

相关代码：

- `src/AcceptanceSpecSystem.Application/Services/SmartConfigurationAppService.cs:274`
- `src/AcceptanceSpecSystem.Application/Services/SmartConfigurationAppService.cs:3742`
- `src/AcceptanceSpecSystem.Core/AI/SemanticKernel/OllamaNativeChatCompletionService.cs:285`
- `src/AcceptanceSpecSystem.Core/AI/SemanticKernel/OllamaNativeEmbeddingGenerator.cs:92`

## 五、慢 SQL 定位

### 5.1 应用慢查询日志

| 请求路径 | 最慢 EF 命令 |
|---|---:|
| `/api/ai-services/7/test` | `1,084.88ms` |
| `/api/documents/excel/import` | `6,237.54ms` |
| `/api/matching/batch-execute` | `548.57ms` |

真正需要优先定位的是 Excel 导入中的 `6.237s`。同期表格解析约 `93ms`，因此该延迟属于数据库命令，不属于 Excel 文件解析。

### 5.2 Performance Schema 语句摘要

与 `6.237s` 完全对应的规范化语句：

```sql
DELETE d
FROM DocumentImportExecutions AS d
WHERE d.ExpiresAt <= ?;
```

| 指标 | 值 |
|---|---:|
| 调用次数 | `3` |
| 最大耗时 | `6.237s` |
| 平均耗时 | `2.079s` |
| 累计检查行数 | `4` |
| 累计影响行数 | `3` |
| 未使用索引调用数 | `2` |
| 首次出现 | `2026-08-07 21:16:42` |
| 最后出现 | `2026-08-07 21:16:44` |

三次调用的平均耗时乘以调用数约等于最大耗时，说明主要是其中一次调用发生了约 6.237 秒的尖峰，另外两次接近即时完成。

`NoIndexCalls=2` 不代表缺少索引：

- `AppDbContext` 已为 `DocumentImportExecutions.ExpiresAt` 建立索引。
- 三次调用合计只检查四行，优化器在极小表上选择扫描是合理行为。
- 四行扫描本身无法解释六秒延迟。

调用链：

- `src/AcceptanceSpecSystem.Application/Services/DocumentImportAppService.Idempotency.cs:47`
- `src/AcceptanceSpecSystem.Data/Repositories/DocumentImportExecutionRepository.cs:20`
- `src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs:680`

### 5.3 其他写入延迟信号

| 语句摘要 | 调用数 | 最大耗时 | 平均耗时 |
|---|---:|---:|---:|
| `COMMIT` | `66` | `11.739s` | `1.612s` |
| 插入 `AuditLogs` | `34` | `8.423s` | `0.362s` |
| 更新 `AiServiceConfigs` | `4` | `4.749s` | `1.278s` |
| 插入 `EmbeddingCaches` | `123` | `0.548s` | `0.005s` |

这些摘要是 MySQL 启动以来的聚合数据，不能证明每个最大值都发生在本次导入请求中；但多类少量写入和提交均出现多秒尖峰，说明问题不局限于单条 DELETE 的执行计划。

## 六、锁、持久化与文件 I/O

### 6.1 已排除的数据库内部原因

| 指标 | 值 | 判断 |
|---|---:|---|
| `Innodb_row_lock_waits` | `0` | 没有 InnoDB 行锁等待 |
| `Innodb_row_lock_time` | `0` | 没有累计行锁等待时间 |
| `Innodb_row_lock_time_max` | `0` | 没有单次行锁等待尖峰 |
| `Innodb_log_waits` | `0` | 日志缓冲区没有等待 |
| `Innodb_buffer_pool_wait_free` | `0` | 没有等待空闲缓冲页 |
| `Innodb_data_pending_fsyncs` | `0` | 采样时没有待处理 fsync |

应用慢查询摘要中的 `TotalTableLockSeconds=0` 也没有显示表锁等待。

### 6.2 持久化配置

| 配置 | 值 |
|---|---:|
| `innodb_flush_log_at_trx_commit` | `1` |
| `sync_binlog` | `1` |
| `log_bin` | `1` |

当前配置要求事务提交时同步保证 redo log 和 binlog 持久性，属于生产数据安全优先的配置。不得仅为降低延迟而关闭 doublewrite、binlog，或降低上述持久化级别。

### 6.3 MySQL 文件等待

| 文件事件 | 调用数 | 累计等待 | 平均等待 | 最大等待 |
|---|---:|---:|---:|---:|
| `innodb_dblwr_file` | `4,842` | `779.401s` | `160.967ms` | `9.400s` |
| `innodb_data_file` | `137,050` | `853.368s` | `6.227ms` | `8.567s` |
| `innodb_log_file` | `9,002` | `684.521s` | `76.041ms` | `7.328s` |
| `sql/binlog` | `541` | `36.627s` | `67.703ms` | `3.075s` |
| `innodb_temp_file` | `168` | `6.934s` | `41.274ms` | `0.801s` |

这些多秒级文件等待与 DELETE、COMMIT 和单行写入的延迟数量级一致。结合零行锁等待和极小的数据扫描量，数据库写入尖峰应归因于底层文件 I/O，而不是 SQL 计算或锁竞争。

## 七、Docker 与物理存储证据

### 7.1 MySQL 数据卷

| 项目 | 值 |
|---|---|
| Docker volume | `acceptancespecrecognition_mysql-data` |
| 容器挂载点 | `/var/lib/mysql` |
| 容器逻辑磁盘 | `/dev/sdc` |
| 逻辑容量 | 约 `1007GB` |
| 已用空间 | 约 `67GB`，使用率 `7%` |
| inode 使用率 | `1%` |

容量和 inode 均充足，不存在磁盘满导致的当前故障。

### 7.2 容器资源快照

| 指标 | 值 |
|---|---:|
| CPU | `0.42%` |
| 内存 | `648.5MiB / 62.65GiB` |
| Block I/O | `15.2MB / 1.42GB` |
| PIDs | `46` |

采样时 MySQL 没有 CPU 或内存压力。

### 7.3 Docker Desktop 虚拟磁盘

| 项目 | 值 |
|---|---|
| Docker 数据 VHDX | `D:\docker\wsl\DockerDesktopWSL\disk\docker_data.vhdx` |
| VHDX 实际大小 | `80.2GB` |
| Docker Desktop 主发行版 | `D:\docker\wsl\DockerDesktopWSL\main\ext4.vhdx` |
| 所在物理盘 | `ST2000DM008-2UB102` |
| 介质类型 | HDD |
| 健康状态 | `Healthy / Online` |

Windows 磁盘健康状态只表示设备没有报告明显硬件故障，不代表随机同步写入延迟满足数据库负载要求。

宿主机同时存在一块 `BG6 KIOXIA 256GB` SSD，但 `C:` 仅剩约 `71.1GB`，小于当前 `80.2GB` Docker 数据 VHDX，不能直接把现有 Docker 数据迁移到该分区。

生产 SQL 备份目录 `D:\AcceptanceBackups` 与 Docker VHDX 位于同一块机械硬盘。在线备份、隔离恢复或大文件复制期间，会与生产 MySQL 争用同一物理磁盘 I/O。现有 Performance Schema 累计值也包含部署和备份时间段，分析总量时必须考虑这一因素。

## 八、根因链

### 8.1 智能识别

```text
智能结构识别请求
→ 规则解析约几十至数百毫秒
→ 单次 qwen2.5:14b 长提示词调用约 19.3 秒
→ 请求总耗时约 20 至 21 秒
```

### 8.2 Excel 导入

```text
Excel 导入请求
→ 幂等入口同步清理过期 DocumentImportExecutions
→ DELETE 产生独立自动提交写入
→ redo log / doublewrite / binlog 在机械硬盘 VHDX 上同步落盘
→ 某次文件 I/O 尖峰导致 DELETE 阻塞约 6.237 秒
→ 后续解析和导入继续执行
```

## 九、下周优化建议

后续优化会改变性能行为或运行架构，实施前应单独创建并审批 OpenSpec 变更；本分析文档不代表已经批准实施。

### P0：移除导入请求中的同步过期清理

目标：不再让用户导入请求承担全局 TTL 清理延迟。

建议方向：

- 将 `DocumentImportExecutions` 过期清理迁移到宿主管理的周期后台任务。
- 使用限频和小批量删除，避免多个并发导入重复执行同一清理。
- 保留 `ExpiresAt` 索引和请求键唯一索引。
- 后台清理失败不得导致导入失败，但必须有脱敏告警和可重试策略。
- 不使用脱离宿主生命周期的 fire-and-forget 任务。

该改动预计能消除本次导入前的约 6.2 秒偶发阻塞，但无法解决其他数据库写入因机械硬盘产生的延迟。

### P1：将 Docker 数据盘迁移到容量充足的 SSD/NVMe

建议方向：

- 使用容量和寿命满足数据库持续写入要求的 SSD/NVMe，预留业务增长、镜像、构建缓存和 VHDX 扩张空间。
- 通过 Docker Desktop 支持的磁盘镜像迁移流程执行，不手工移动正在使用的 VHDX。
- 迁移必须另开维护窗口，先完成数据库备份及隔离恢复验证，再停止应用和 Docker Desktop。
- 迁移后重新核对 MySQL volume、API 文件卷、数据保护密钥卷和应用备份卷。
- 迁移后保留原备份点，完成数据库、容器、readiness 和浏览器业务验收后再讨论清理。

当前 `C:` 空间不足，不应作为迁移目标。更合适的方案是新增或扩容独立 SSD/NVMe。

### P1：将备份与生产数据分离到不同物理介质

- 在线备份和隔离恢复尽量避开业务高峰。
- 备份目标优先选择另一块物理磁盘、NAS 或受控备份存储。
- 不因性能原因删除现有已验证备份或回退镜像。

### P2：建立 LLM 性能与质量基线

- 使用固定、脱敏的代表性文档建立识别质量和耗时基线。
- 分别测量提示词处理、模型 eval、输出长度、结构化解析和重试次数。
- 对比提示词裁剪、候选信息压缩、模型量化或更小模型时，必须同时验证识别正确率，不能只比较速度。
- 保持模型常驻和现有真实 readiness 探测，避免把冷启动重新引入用户请求。
- 在没有质量验收数据前，不直接替换生产模型。

### P2：补充可观测性

- 为导入阶段增加脱敏的分段耗时：幂等清理、幂等查询、文件授权、表格解析、规格加载、重复检测、SaveChanges、Commit 和源文件清理。
- 为 MySQL 写入延迟建立可观测指标，区分 SQL 执行、事务提交和存储等待。
- 保持 `SlowQuery.IncludeSqlText=false`，避免 SQL 参数或业务数据进入日志。
- 使用规范化 digest 或安全的语句标识关联慢查询，不记录客户原始内容。

## 十、后续验收建议

应用优化后至少验证：

1. 导入请求不再同步执行全局过期记录清理。
2. 并发导入仍保持请求幂等，唯一键冲突行为不变。
3. 后台清理能删除过期记录，失败可重试且不影响导入主流程。
4. 相同测试文件下，上传、解析、导入结果与修改前一致。
5. Performance Schema 中导入请求不再出现对应 DELETE 的同步慢查询。

存储迁移后至少验证：

1. Docker Desktop 数据 VHDX 位于目标 SSD/NVMe。
2. 原 Compose 项目和所有生产卷身份保持不变。
3. MySQL 关键表检查、迁移历史和业务行数正常。
4. API、Web、MySQL 均为 `healthy`，readiness 组件全部健康。
5. 使用相同工作负载重新采集 COMMIT、redo log、doublewrite、binlog 最大和平均等待。
6. 浏览器抽查客户、制程、机型、验收规格、模板、文件、导入和匹配流程。

LLM 优化后至少验证：

1. 使用同一批固定样本比较识别耗时。
2. 检查识别列映射、表头范围、推荐动作和 AI 辅助状态。
3. 确认没有新增超时、重试、HTTP `499`、HTTP `5xx` 或 GPU OOM。
4. 性能改善不能以识别质量下降或静默降级为代价。

具体 P95、P99 和质量门槛应在后续 OpenSpec 中依据固定样本基线确定，本分析不凭经验设定任意阈值。

## 十一、安全约束

- 不执行 `docker compose down -v`。
- 不删除或重建生产 MySQL 容器和数据卷。
- 不手工修改 `__EFMigrationsHistory`。
- 不覆盖生产 `.env.docker`。
- 不通过关闭 doublewrite、binlog 或降低事务持久化级别换取延迟改善。
- 不手工移动运行中的 Docker VHDX。
- 不在日志或分析文档中记录密码、JWT、连接字符串、客户数据或 SQL 参数值。
- 存储迁移和生产代码部署都必须作为独立变更重新走备份、恢复证明、回退镜像和健康验收流程。

## 十二、最终判断

| 判断 | 置信度 |
|---|---|
| 智能结构识别约 20 秒主要来自 LLM 推理 | 高 |
| LLM 慢不是模型冷加载导致 | 高 |
| Excel 导入 6.237 秒来自过期幂等记录 DELETE | 高 |
| DELETE 慢不是缺少 `ExpiresAt` 索引 | 高 |
| DELETE 慢不是 InnoDB 行锁等待 | 高 |
| 数据库多秒写入尖峰来自 Docker VHDX 底层 I/O | 高 |
| 机械硬盘上的 Docker 数据盘是主要基础设施限制 | 高 |
| 某次尖峰由哪一个具体宿主进程或写入事件触发 | 未验证 |

本报告用于保留下周优化前的可复核基线。后续实施应分别处理“LLM 推理延迟”“导入请求内同步清理”和“Docker 数据盘介质”三个问题，避免把它们合并为单一性能故障。
