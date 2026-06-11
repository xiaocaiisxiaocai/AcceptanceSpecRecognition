# vLLM 部署指南：LLM + Embedding 双服务

> 创建时间：2026-06-10
> 目标环境：单张 RTX 4090（24GB）+ Qwen2.5-14B（LLM 裁决/重排）+ Qwen3-Embedding-4B（召回）
> 关联：[architecture-improvements.md](./architecture-improvements.md) 的 vLLM 改造项

---

## 核心结论

- vLLM **同时支持 LLM 和 Embedding 部署**，都暴露 OpenAI 兼容接口（`/v1/chat/completions`、`/v1/embeddings`）
- **关键约束：一个 vLLM 进程只能加载一个模型**。LLM 和 Embedding 必须起两个独立进程、两个端口
- 单卡 4090 跑双模型时，必须手动用 `--gpu-memory-utilization` 切分显存，否则第一个进程会吃满显存导致第二个起不来

---

## 显存预算（4090 24GB）

| 模型 | 量化 | 权重显存 | 分配比例建议 |
|------|------|---------|------------|
| Qwen2.5-14B-Instruct | AWQ (4bit) | ~9-10 GB | `--gpu-memory-utilization 0.65` |
| Qwen3-Embedding-4B | fp16 或 AWQ | ~8 GB(fp16) / ~3 GB(AWQ) | `--gpu-memory-utilization 0.25` |
| 系统/显示/余量 | — | ~1-2 GB | 预留 |

> 注意：`gpu-memory-utilization` 是"该进程可用显存占总显存的比例"，两个进程相加要 < 0.95，给系统留余量。
> 若 Embedding 用 fp16 显存吃紧，优先把 Embedding 也换成 AWQ/GPTQ 量化版本。

---

## 第一步：环境准备

```bash
# 建议独立虚拟环境，Python 3.9-3.12
python -m venv vllm-env
source vllm-env/bin/activate    # Windows: vllm-env\Scripts\activate

# 安装 vLLM（自带 CUDA 依赖，需 NVIDIA 驱动支持 CUDA 12.x）
pip install vllm

# 验证
python -c "import vllm; print(vllm.__version__)"
```

> Windows 原生对 vLLM 支持有限，**强烈建议在 WSL2 (Ubuntu) 或 Linux 下部署**。
> 若必须 Windows，考虑用 Docker 镜像 `vllm/vllm-openai`。

---

## 第二步：启动 LLM 服务（端口 11434）

```bash
python -m vllm.entrypoints.openai.api_server \
  --model Qwen/Qwen2.5-14B-Instruct-AWQ \
  --quantization awq \
  --served-model-name qwen2.5-14b \
  --max-model-len 8192 \
  --max-num-seqs 16 \
  --gpu-memory-utilization 0.65 \
  --port 11434
```

参数说明：
- `--served-model-name`：API 里用的模型名，请求时 `model` 字段填这个
- `--max-model-len`：单请求最大上下文，裁决场景 8192 足够
- `--max-num-seqs`：最大并发请求数，连续批处理的并发上限
- `--gpu-memory-utilization 0.65`：本进程最多用 65% 显存

---

## 第三步：启动 Embedding 服务（端口 11435）

```bash
python -m vllm.entrypoints.openai.api_server \
  --model Qwen/Qwen3-Embedding-4B \
  --task embed \
  --served-model-name qwen3-embedding-4b \
  --gpu-memory-utilization 0.25 \
  --port 11435
```

参数说明：
- `--task embed`：以 Embedding 模式启动（新版 vLLM 用 `--runner pooling`，按版本而定）
- 不需要 `--max-num-seqs`，Embedding 默认批处理

> 若 Qwen3-Embedding-4B 无官方 AWQ 版，先用 fp16，把 `--gpu-memory-utilization` 调到 0.30 试启动。
> 显存不足报 OOM 时，下调 LLM 的比例或减小 `--max-model-len`。

---

## 第四步：验证两个服务

```bash
# 验证 LLM
curl http://localhost:11434/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"model":"qwen2.5-14b","messages":[{"role":"user","content":"测试"}]}'

# 验证 Embedding
curl http://localhost:11435/v1/embeddings \
  -H "Content-Type: application/json" \
  -d '{"model":"qwen3-embedding-4b","input":"工作电压 DC 12V"}'
```

---

## 第五步：改本系统配置（业务代码零改动）

在 `/config/` 页面 → AI 服务配置：

| 服务用途 | BaseUrl | 模型名 |
|---------|---------|--------|
| LLM（裁决/重排） | `http://localhost:11434/v1` | `qwen2.5-14b` |
| Embedding（召回） | `http://localhost:11435/v1` | `qwen3-embedding-4b` |

> 系统通过 Semantic Kernel 的 OpenAI 兼容连接器调用，只改 BaseUrl + 模型名即可，
> `SemanticKernelMatchingService` / `LlmMatchingAssistService` 代码无需改动。

---

## 进程守护（生产）

两个服务建议用 `systemd`（Linux）或 `nssm`（Windows）做进程守护，避免崩溃后不自动重启。

systemd 示例（`/etc/systemd/system/vllm-llm.service`）：

```ini
[Unit]
Description=vLLM LLM Service
After=network.target

[Service]
ExecStart=/path/to/vllm-env/bin/python -m vllm.entrypoints.openai.api_server \
  --model Qwen/Qwen2.5-14B-Instruct-AWQ --quantization awq \
  --served-model-name qwen2.5-14b --max-model-len 8192 \
  --max-num-seqs 16 --gpu-memory-utilization 0.65 --port 11434
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

---

## 风险与权衡（务必先评估）

🟡 **单卡跑双模型，显存是硬约束**
- 14B AWQ + 4B Embedding fp16 在 24GB 上偏紧，必须实测能否同时起来
- 若起不来，备选方案：Embedding 继续留在 Ollama，只把 LLM 迁到 vLLM（显存不打架，部署更简单）

🟡 **Windows 原生支持差**
- 优先 WSL2 / Docker，否则可能踩 CUDA 编译坑

🟢 **收益主要在 LLM 并发**
- vLLM 对 LLM 的连续批处理提升明显（这是迁移的主因）
- Embedding 调用本身快且已有缓存预热，是否迁 vLLM 收益有限——可后置

---

## 推荐落地顺序

1. **先只迁 LLM 到 vLLM**（解决并发瓶颈这一主要痛点），Embedding 暂留 Ollama
2. 观察显存余量与 LLM 吞吐改善
3. 若确认 Embedding 也成瓶颈，再单独给它上 vLLM（端口 11435）

> 不建议一上来就双 vLLM——单卡显存紧张，分两步走更稳。
