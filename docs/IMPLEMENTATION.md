# 首版实施与验收

权威设计：[DESIGN.md](DESIGN.md)。本轮用户已授权初始化本地 Git 仓库并实现首版；设计中原先的“只写文档”是历史阶段限制。

## 边界与接口

实现内存中 JSON DOM 与 md-json 的完整闭环；不包含设计 §9 的暂缓功能。依赖方向为调用方 → MdJson → System.Text.Json / Markdig。不引入 CLR 反射框架。

公开接口冻结为 `Atelia.MdJson.MdJsonSerializer.Write(JsonElement value, IReadOnlyList<string> externalStringPaths)` 和 `Read(string markdown) -> JsonElement`。Read 返回脱离临时 JsonDocument 生命周期的元素。输入不修改；格式、路径、支持域错误统一为 `FormatException`，null 参数为 `ArgumentNullException`。

内部接缝：`JsonValues.Validate(JsonElement)` 验证全树唯一键与有效 Unicode；`Resolve(JsonElement, string)` 定位；`Replace(JsonElement, IReadOnlyDictionary<string,string>) -> JsonElement` 按规范 Pointer 替换字符串并保持数字原文与顺序。Reader 先验证全部块再调用 Replace。方法均无共享可变状态。

## 需求映射

| 需求 | 实现归属 | 验收 | 状态 |
| --- | --- | --- | --- |
| §2–3 值域、Pointer、非修改、数值与顺序 | writer agent / JsonValues | 独立 public API 测试 | 已验收 |
| §4、6 输出、路径列表顺序、动态围栏 | writer agent / Writer | 输出断言与字符往返 | 已验收 |
| §4–7 Markdig 块、源码覆盖、严格解析与回填 | reader agent / Reader | 非法文档与 CR/NUL 边界测试 | 已验收 |
| §8 用户场景与所有验收类别 | test agent / tests | Debug / Release 测试 | 已验收 |
| 初始化、构建配置、README、独立审查 | 主线程 | build、测试、审查、工作树检查 | 已验收 |

高风险不变量：只剥离一个 LF、原始源码切片、引用不递归、完整删块允许的边界、严格 JSON 数值不经过 double、隐藏 Markdown 源码不能被忽略。

完成门槛：实际读代码并集成、所有测试通过、独立审查无阻断问题、文档反映真实接口与限制。技术探针由针对实际已还原包的运行测试取代，不能以源码阅读代替运行证据。

## 验收结果（2026-09-15）

- 环境：Windows、.NET SDK 10.0.201、net10.0、Markdig 1.3.2、xUnit 2.9.3。
- `dotnet restore MdJson.slnx` 成功。
- `dotnet test MdJson.slnx --no-restore`：Debug 106/106，无跳过。
- `dotnet build MdJson.slnx -c Release --no-restore`：零警告、零错误。
- `dotnet test MdJson.slnx -c Release --no-build`：Release 106/106，无跳过；含 128 组确定性随机正文往返。
- `dotnet pack src/MdJson/MdJson.csproj -c Release --no-build -o artifacts/packages` 成功生成本地 MdJson 0.1.0 包；未发布。
- 仓库外的独立 net10.0 控制台使用 PackageReference 和独立包缓存，还原本地包及 Markdig，通过 CLR 对象往返、CR/LF/NUL/围栏正文及大整数原文验证。
- 主线程阅读全部实现和测试；独立 reviewer 完成只读审查，未留下阻断问题。

集成中修正并覆盖了两个边界：读端默认 64 层深度与写端不一致（增加 96 层回归）；宽松来源 JsonDocument 的注释可能包含引号或伪转义（以 JSON token 扫描验证实际字符串，输出仍为严格 JSON）。原始 Markdown 在 JSON 解码前检查有效 UTF-16，避免无效代理项被静默替换。

初次 `--no-restore` 测试使用了加入 Markdig 前的测试项目资产，导致运行时缺少传递依赖；完整 solution restore 后全部通过。此项属于初始化过程的依赖资产刷新，不是格式逻辑失败。

首版没有已发布兼容性承诺或 LLM 效果数据。完整删块检测及设计 §9 暂缓项保持原边界。Git 已初始化为 main；未创建提交或配置远端。
