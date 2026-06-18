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

## Docker 部署方式（Windows 11 推荐，2026-06-11 实测记录）

Windows 原生跑 vLLM 易踩 CUDA 编译坑，**推荐用官方镜像 `vllm/vllm-openai` 走 Docker**。用了 Docker 就**跳过下面的「第一/二/三步」原生安装**，直接用本节命令。

### 前提：Docker 能用 GPU

1. 装 **Docker Desktop**（WSL2 后端）；Windows 装好 **NVIDIA 驱动**（支持 WSL2 GPU，无需在 WSL 内单独装驱动）。
2. GPU 直通自检（一整行）：

```
docker run --rm --gpus all nvidia/cuda:12.4.0-base-ubuntu22.04 nvidia-smi
```

能打印出 4090 即正常；这条若报错，先修 Docker Desktop 的 GPU 支持再继续。

### ⚠️ 命令必须写成一整行

`docker run` 命令里的反斜杠 `\` 是 Linux/bash 续行符，**Windows CMD/PowerShell 不认**，分行粘贴会逐行报"不是内部或外部命令"。**务必整行粘贴运行。**

### 起 LLM 容器（宿主端口 11434）

```
docker run -d --name vllm-llm --restart unless-stopped --gpus all --ipc=host -p 11434:8000 -v vllm-cache:/root/.cache/huggingface -e HF_ENDPOINT=https://hf-mirror.com vllm/vllm-openai:latest --model Qwen/Qwen2.5-14B-Instruct-AWQ --quantization awq --served-model-name qwen2.5-14b --max-model-len 8192 --max-num-seqs 16 --gpu-memory-utilization 0.65
```

### 起 Embedding 容器（宿主端口 11435）

```
docker run -d --name vllm-embed --restart unless-stopped --gpus all --ipc=host -p 11435:8000 -v vllm-cache:/root/.cache/huggingface -e HF_ENDPOINT=https://hf-mirror.com vllm/vllm-openai:latest --model Qwen/Qwen3-Embedding-4B --task embed --served-model-name qwen3-embedding-4b --gpu-memory-utilization 0.25
```

### Docker 专属要点（容易踩）

- **`--ipc=host` 必须加**：vLLM 用共享内存做张量通信，不加会报 SHM 错误。
- **`-p 11434:8000`**：镜像内部默认监听 8000，映射到宿主 11434/11435，所以命令里**不写 `--port`**。
- **两个容器都加 `--gpus all`**：共用同一张 4090，靠各自 `--gpu-memory-utilization`（0.65 + 0.25，相加 ≤0.95）切分显存。
- **`-v vllm-cache:...` 共享命名卷**：两容器复用同一份模型，重启/重建不重下；除非显式 `docker volume rm vllm-cache`。
- **`HF_ENDPOINT=https://hf-mirror.com`**：国内走镜像拉模型。仍慢可改用 ModelScope 下到本地目录后 `-v 本地目录:/models --model /models/xxx` 挂载。
- **`--restart unless-stopped`**：开机随 Docker Desktop 自动拉起、崩溃自动重启；手动 `docker stop` 后则保持停止。

### 生命周期（装一次，之后免敲命令）

- `docker run` 只跑**一次**（再跑会同名报错）。之后让 **Docker Desktop 开机自启**（Settings → General → Start when you log in），容器即随之自动起。
- 日常命令：

```
docker ps                              # 看是否 Up
docker logs -f vllm-llm                # 看模型加载 / OOM
docker stop vllm-llm vllm-embed        # 临时释放显卡
docker start vllm-llm vllm-embed       # 重新拉起
docker rm -f vllm-llm                  # 改参数前先删旧容器再重新 run
```

### 验证

```
docker ps
curl http://localhost:11434/v1/models
curl http://localhost:11435/v1/models
```

> 接本系统：API 跑在 Windows 宿主/IIS 时用 `http://localhost:11434/v1`（Docker Desktop 转发端口到 Windows localhost）。
> 若 .NET API 也在 Docker 内，则改用 `http://host.docker.internal:11434/v1` 或同一 Docker 网络。服务类型仍选 **LM Studio**（见第五步）。

---

## 第一步：环境准备

> 📌 **若用上面的 Docker 方式部署，第一/二/三步（原生安装）整段跳过，直接到第四步验证。** 本段仅适用于不走 Docker 的原生安装。

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

| 服务用途 | 服务类型 | BaseUrl | 模型名 |
|---------|---------|---------|--------|
| LLM（裁决/重排） | **LM Studio** | `http://localhost:11434/v1` | `qwen2.5-14b` |
| Embedding（召回） | **LM Studio** | `http://localhost:11435/v1` | `qwen3-embedding-4b` |

> ⚠️ **服务类型必须选 "LM Studio"，不能选 "Ollama" 或 "OpenAI/自定义兼容"**（源码核对结论）：
> - 系统对 `Ollama` 类型走**原生 Ollama 协议**（`OllamaNativeChatCompletionService`），vLLM 只暴露 OpenAI 兼容接口，选 Ollama 会调不通。
> - `OpenAI` / `CustomOpenAICompatible` 类型在 `AiEndpointNormalizer` 里**禁止 localhost/内网地址**（保存和"测试连接"都会报"不允许使用本地或内网地址"）。
> - 只有 `LMStudio` 类型同时满足：① 允许 localhost；② 走标准 OpenAI 兼容连接器（`AddOpenAIChatCompletion`）。这正是 vLLM 需要的。
>
> 系统通过 Semantic Kernel 的 OpenAI 兼容连接器调用，只改服务类型 + BaseUrl + 模型名即可，
> `SemanticKernelMatchingService` / `LlmMatchingAssistService` 代码无需改动。

---

## 第五步补充：卸载 Ollama 的正确顺序（务必先验证再卸载）

若计划彻底卸载 Ollama，则 LLM 和 Embedding **都必须**迁到 vLLM（不能再用"Embedding 留 Ollama"的过渡方案），单卡双模型显存约束变成硬性前提。推荐顺序：

1. **保留 Ollama 不动**，先把 vLLM 两个服务起在新端口（11434/11435），用第四步的 curl 验证能正常返回。
2. 在 `/config/` **新增**两条 LM Studio 类型的 AI 服务配置（指向 vLLM），**先不要删旧的 Ollama 配置**。
3. 在智能填充页面跑一次真实文档预览，确认匹配/裁决走 vLLM 正常、显存不 OOM。
4. 确认无误后，再把旧的 Ollama AI 服务配置停用/删除，最后 `ollama rm <model>` + 卸载 Ollama 本体。
5. **切勿先卸载 Ollama**：一旦先卸载，若 vLLM 显存起不来或配置有误，系统将完全没有可用 AI 服务，智能填充直接不可用。

> 显存提醒：卸载 Ollama 后显卡完全空出，4090 跑 14B-AWQ + 4B-Embedding 更宽裕；但仍须在卸载前实测两个 vLLM 进程能同时常驻。

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
