# Jev-Style 本地安装与验证 — 2026-10-04

本次按人类负责人要求接手 Jev 安装。已在 Windows 本机安装并验证作者发布的 **Jev-Style-0.8B-Decision-v3**，使用官方 PyTorch 推理脚本和校准配置。它是 Jev-Style 项目的本地模型，不是托管 Jev 服务。

## 安装位置与版本

- 独立环境：`C:\Users\USER\Documents\JevStyleLocal`
- Python：3.12.14；`jev-style`：0.4.0；PyTorch：2.14.1+cpu；Transformers：5.18.0。
- 模型：`chaoliangUNSW/Jev-Style-0.8B-Decision-v3`。
- 固定 revision：`b023d1f9c7858fbf01504577a3bfc349ea5c7385`。
- 权重：`model.safetensors`，1,504,827,608 bytes，作者发布的 BF16 权重；运行时加载为 FP32。
- 权重 SHA256：`0f8c861605dcdfb356a63e056baa2106e81042e8981e8d0d26fe50b34599541e`，与官方 manifest 相符。
- 当前后端：CPU，4 个计算线程；CUDA 尚未安装/验证。
- 依赖锁：环境根目录的 `requirements-lock.txt`。

没有安装 Visual Studio Build Tools，也没有编译 llama.cpp。旧 Transformers 4.41.2 不支持这款架构，不能据此判断当前 Transformers 路径不可用。此次独立环境已完成真实推理。

## 启动、调用与停止

交付时服务正在运行，仅监听 `127.0.0.1:8765`，无开机启动项。模型加载完成后在离线模式下运行，未上传项目内容。

```powershell
# 检查状态
Invoke-RestMethod http://127.0.0.1:8765/healthz

# 服务停止后启动；前台运行时保持该终端打开
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Users\USER\Documents\JevStyleLocal\start-jev.ps1

# 用 JSON 文件提交设计/AI 参考问题
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Users\USER\Documents\JevStyleLocal\ask-jev.ps1 -RequestPath C:\Users\USER\Documents\JevStyleLocal\example-request.json

# 停止记录的 Jev 服务，脚本会核实 PID 与脚本路径
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Users\USER\Documents\JevStyleLocal\stop-jev.ps1
```

浏览器测试页面：`http://127.0.0.1:8765/`。推理接口：`POST /v1/systemone`。请求含 `state` 和 `questions`；问题支持 `choice`、`noul`、`score`。环境中的 `example-request.json` 是明确标注的合成测试，不能作为实际游戏快照或胜率证据。

## 已执行验证

1. `pip check`：无依赖冲突。
2. `verify_jev.py`：作者 manifest 的 12 项运行文件哈希全部通过；中英文真实推理成功；概率有限且归一化；过长输入与重复选项被拒绝。
3. 独立计算完整词表 logits，对比官方方向向量打分 `logit(yes) - logit(no)`：最大绝对误差 `2.384185791015625e-06`；softmax 概率误差 `2.220446049250313e-16`。
4. 验证 yes/no token ID 为 9542/874、20 组温度配置和分组查找。
5. `verify_api.py`：健康检查、模型身份、中文三种题型、HTTP 422 非法输入处理、Playground HTTP 200 均通过。对齐校准类别后，API 与直接运行时的概率最大误差 `4.392864771940452e-08`。

直接推理且不指定类别时，全局温度为 **0.8800546821789332**。官方 HTTP Adapter 默认类别为 **typed_official**，会按题型及选项数量选择分组温度；例如三选一使用 **0.9751541851571508**。比较输出时必须记录入口和类别，不能把这些概率差异误判为换模型或协议错误。API 的 `confidence` 是作者定义的概率集中程度，并不等于最高选项的概率，也不是实测准确率。

短输入的单题 CPU 实测约 1.4–2.5 秒；三题中文 API 请求约 6.9 秒。未安装可选的加速内核，使用 PyTorch 参考实现。没有完成 CUDA 加速、长上下文性能或游戏决策质量评测。

完整证据位于独立环境的 `evidence/smoke-report.json`、`evidence/api-report.json` 及服务日志。复现命令：

```powershell
& C:\Users\USER\Documents\JevStyleLocal\venv\Scripts\python.exe C:\Users\USER\Documents\JevStyleLocal\verify_jev.py
& C:\Users\USER\Documents\JevStyleLocal\venv\Scripts\python.exe C:\Users\USER\Documents\JevStyleLocal\verify_api.py
```

## 项目参考边界

模型现在可供本机脚本、PL 或 QA 提交设计分类、候选方案选择及有序评分问题。正式游戏 AI 接入尚未实施。接入时应使用引擎给出的、符合当前玩家信息边界的状态与合法行动，把模型返回的候选 ID 再交给引擎校验；规则、合法性、目标与胜负仍由权威引擎判断。游戏专用标注集、战术正确率、决策延迟和对局效果需另行验证。

此次未修改游戏生产源码、规则、卡牌、数值、其他人的既有工作、模型服务凭据或 relay 配置；未提交或推送 Git。未启动 PL/QA 付费接力，也未将本服务注册到 Harness/Codex MCP 配置。

## 来源

- [Jev-Style 作者仓库及安装说明](https://github.com/lawrence3699/jev-style)
- [模型固定版本](https://huggingface.co/chaoliangUNSW/Jev-Style-0.8B-Decision-v3/tree/b023d1f9c7858fbf01504577a3bfc349ea5c7385)
