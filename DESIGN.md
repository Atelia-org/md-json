# md-json 设计草案

状态：首版已实现并通过运行验收，尚未发布。本文是当前设计入口；早期审视记录见 [DESIGN-REVIEW.md](DESIGN-REVIEW.md)，实现与验收见 [IMPLEMENTATION.md](IMPLEMENTATION.md)，使用示例见 [README.md](README.md)。

## 1. 目标与需求来源

md-json 为 JSON 值树提供一种 Markdown 文本表示：保留 JSON 骨架，将调用方选定的字符串值移到代码围栏中，以 JSON Pointer 标明回填位置。主要用途是在生成 LLM 请求时临时渲染 Observation、Dynamic Recall 等结构化内容。

```text
CLR 对象 ⇄ System.Text.Json ⇄ JSON 值树 ⇄ md-json 文本
```

### 需求账本

| 编号 | 来源 | 当前要求或证据 |
| --- | --- | --- |
| U1 | 用户首轮描述 | .NET 10.0 类库、xUnit、Markdig 解析 Markdown；正文无需转义，文本可无损转回内存对象；内部项目使用。 |
| U2 | 用户第二轮提出并在第三轮继续采用 | JSON 骨架 + 外置字符串正文；保留既有 CLR/JSON 分层，可只在请求生成时接入，不改变存储。 |
| U3 | 用户本轮决定 | 使用 JSON Pointer；渲染时显式传入待外置路径列表；解析时逐项回填；首版一块正文只对应一个位置。 |
| U4 | 用户前一阶段工作范围 | 先写设计文档，再用 dialectical-simplification 改进；当时不包含类库实现。 |
| U5 | 用户本轮实施授权 | 初始化本地仓库、目录、slnx、.NET 10 类库和 xUnit 测试；由 subagents 协作完成本文首版。 |
| C1 | 初始目录检查 | 目录原为空，无既有代码、已发布数据或现存消费者；现有实现由 U5 授权建立。 |
| D1 | JSON Pointer 定位语义 | 被引用的成员名不唯一时，路径求值失败；RFC 不要求未引用子树的所有键都唯一。见 §10。 |
| P1 | 草案提议，经本轮审视保留 | 正文标题是回填路径的唯一清单；不增加文内 manifest；完整删除正文块不保证可检测。见 §5。 |
| P2 | 草案提议，经本轮审视保留 | 固定 LF 分隔字符、完整文档解析、严格区段语法、保持属性顺序与数字精度。 |
| P3 | 本项目的首版支持域选择 | JSON 对象属性名唯一，字符串是有效 Unicode；不承诺覆盖 System.Text.Json 能接纳的每一种原始 JSON 文本。 |

运行模型：在内存中处理完整值树和完整文本，一次调用返回结果或失败。没有并发共享状态、持久化、崩溃恢复或网络协议职责。库支持程序写入、LLM 阅读和程序读回；LLM 自行生成该格式的可靠性尚无验证。

## 2. 最小数据模型与接口边界

仅三个概念：JSON 骨架、待外置 JSON Pointer 列表、带路径的字符串正文块。路径同时充当地址和标识，无独立 ID、引用图或内容去重。

概念接口：

```text
Write(value, externalStringPaths) -> markdown
Read(markdown) -> value
```

- 首版公开接口为 `MdJson.MdJsonSerializer.Write(JsonElement value, IReadOnlyList<string> externalStringPaths)` 与 `Read(string markdown) -> JsonElement`。Read 返回独立于临时 JsonDocument 生命周期的元素。
- `value` 是对象属性名唯一的 JSON 值树，根可以是任意 JSON 值。格式、路径或支持域错误抛出 `FormatException`；null 参数（含路径列表中的 null 项）抛出 `ArgumentNullException`。
- `externalStringPaths` 是精确路径列表；空列表表示所有值保留在 JSON 中。
- 路径列表只影响表示形式，不改变恢复后的数据。
- 写入不修改调用方的值树。读回只返回恢复后的值树，不恢复渲染路径列表或原 Markdown 排版。
- 不新增业务正文类型；Observation、Recall、source 等都是调用方字段。

## 3. 路径与外置选择

使用 RFC 6901 的 JSON Pointer 字符串形式，不使用 URI fragment、点路径、通配符或查询表达式。

- `/observation/0/intention` 定位数组第一个元素的属性。
- JSON 解码得到路径后，先按 `/` 分 token，再对每个 token 按 RFC 顺序解码一次：`~1` 变 `/`，`~0` 变 `~`。例如 `/a~1b` 定位键 `a/b`，`/~01` 定位键 `~1`；不能先解码再分段或反复解码。
- 空路径 `""` 指向根，`/` 指向根对象的空名称属性。
- 数组下标为规范十进制形式，禁止前导零和越界；不支持数组追加位置 `-`。对象的 `"01"`、`"-"` 等普通键仍可定位。
- 写入前验证所有路径：路径重复、不存在、语法错误或目标不是字符串，都失败，不静默跳过。
- 输入值树与解析得到的骨架都遵守 §2 的唯一键支持域；超出支持域时拒绝，不静默保留重复成员中的某一个。属性名按原字符匹配，不做大小写或 Unicode 归一化。
- 首版不提供自动选择、命名约定、正则、属性标注、策略接口。以后这些机制可在调用侧生成同样的显式路径列表。

正文按 `externalStringPaths` 的传入顺序输出，不再执行另一套排序。需要与骨架顺序一致时，调用方按该顺序传入列表。骨架保留输入对象属性顺序与数组顺序；读回结果不依赖正文块的排列。

## 4. 文档形状

固定两个顶层区段，顺序为 `# structure`、`# content`。structure 内恰好一个 `json` 围栏；content 内为零个或多个“路径标题 + text 围栏”。空路径列表仍输出空的 content 区段。

本例渲染参数依次为 `/observation/0/intention`、`/recall/0/content`。原正文均不含末尾 LF，闭围栏直接位于正文最后一行的下一行；不额外插入空白行。精确字符规则见 §6。

`````markdown
# structure

~~~json
{
  "observation": [
    {"id": "42", "name": "张三", "intention": "/observation/0/intention"}
  ],
  "recall": [
    {"timestamp": "2026-09-15", "content": "/recall/0/content"}
  ]
}
~~~

# content

## "/observation/0/intention"

~~~text
喝口水，继续说道：
“我跟你说呀！”
“那天我在后山看见了一个……”
~~~

## "/recall/0/content"

~~~text
张三常常吹牛皮。
~~~
`````

写入规则：在被选中的位置 `p`，把原字符串替换为字符串 `p`；将原字符串写到标题为 `p` 的正文块中。相同正文出现在两个位置时，分别输出两个块。

路径标题精确为 `## ` 后接一个完整的 JSON 字符串字面量。先解析 JSON 字符串，再解析 JSON Pointer；特殊键名里的引号、换行等沿用 JSON 编码。标题按原始源码读取，不从 Markdown inline 渲染结果恢复路径。

普通 meta 留在合法 JSON 内，采用 JSON 必需的转义；生成器应使常见可打印 Unicode 可直接阅读。骨架固定使用 `~~~json` 与 `~~~`：严格 JSON 中的字符串有引号，CR/LF 必须转义，因此其中的围栏字符不能形成独占一行的裸闭围栏。动态围栏只用于原样正文。

## 5. 回填与引用判定

正文标题集合是本文件内唯一的回填清单。普通字符串即使恰好是合法路径，也不自动成为引用。

对每个正文块 `(p, text)`：

1. 在骨架中解析路径 `p`，要求它定位现有字符串节点。
2. 要求该字符串值按 ordinal 比较恰好等于 `p`，用来检查正文与占位位置是否对应；这只是局部格式校验，不是完整性保证。
3. 将该位置恢复为 `text`；同一路径只能有一个正文块。

实际实现可先验证再构造结果；失败时不返回部分恢复结果。回填内容不再次按路径求值，也不把正文里的标题解释为新块。无需建立通用图解析或递归解引用机制。

### 有意保留的完整性边界

原值 `{"x":"/x"}` 配合空正文区段，是合法文档。把原值 `{"x":"任意正文"}` 外置后，再完整删除 `/x` 的正文标题和围栏，也可能形成同一个合法文档。因此：

- 正常 `Read(Write(value, paths))` 必须无损。
- 不保证检测“完整正文块被删除”这种仍形成合法文档的修改。
- 缺少显式闭围栏、重复正文、无效路径等已知语法或定位错误必须拒绝。

若以后要求检测缺失的完整正文块，再评估位于业务数据之外的路径清单；它不属于当前已确定需求。只在原值内加入 `$ref` 对象也不能自动解决任意业务对象与标记碰撞的问题。

## 6. 围栏与字符串无损

### 围栏选择

对待包裹的原字符串分别计算反引号与波浪线的最长连续长度，候选长度为 `max(3, maxRun + 1)`。选择长度较短者，等长优先波浪线。开闭围栏顶格、字符和长度相同，正文块标签为 `text`。骨架围栏按 §4 固定输出，不扫描骨架的最长连续字符。

扫描全部字符即可，不引入“只扫描可能闭合的行”的优化。完整 payload 已在内存中，因此不承诺未知流的边读边写。

### 正文的精确 framing

每个 text 块的字符布局严格为：

```text
openingFence + "text" + LF + originalString + LF + closingFence
```

这里两个 LF 是协议分隔字符。无论原字符串是否以换行结束，都额外写入第二个 LF。解析时在原始输入字符串中提取“开围栏行 LF 之后、闭围栏首字符之前”的区域，要求区域以 LF 结尾，并且只去掉最后一个 U+000A。

| 原值（JSON 表示） | 围栏之间的区域（JSON 表示） | 恢复动作 |
| --- | --- | --- |
| `""` | `"\n"` | 去掉一个 LF 得到空字符串 |
| `"a"` | `"a\n"` | 得到 `a` |
| `"a\n"` | `"a\n\n"` | 保留原 LF |
| `"a\r"` | `"a\r\n"` | 只去掉 LF，保留原 CR |
| `"a\r\n"` | `"a\r\n\n"` | 保留原 CRLF |

不使用 Trim、不重组代码块行、不经过 HTML。正文中的空格、制表符、CR、LF、CRLF 和 Markdown 符号保持原字符。骨架是 JSON 编码，其闭围栏前 LF 属于 JSON 可忽略空白，不套用字符串回填规则。

结构行使用 LF；任意正文换行保持原状。逐字符无损以输入文本未被编辑器、传输或换行转换器改写为前提。支持域为 JSON 值树中的有效 Unicode 字符串，包括 JSON 可表达的控制字符；任意无效 UTF-16 序列的 CLR 字符串不在此承诺内。U+0000 等必须从保留的原始输入恢复，不能信任 Markdown 的规范化文本。

无损不要求保留原 JSON 的缩进、转义拼写或原 Markdown 的围栏样式。保留值类型、字符串字符、数组顺序和对象输入属性顺序；数字不得经 double 等窄化表示丢失精度。实现使用 JsonElement 与 Utf8JsonWriter，并以数字原文拷贝；运行测试覆盖大整数、高精度小数、指数与负零。

## 7. Markdig 与语义层

Markdig 识别块与源码位置；语义层只接受 §4 的文档形状，并从原始输入提取标题和正文。Markdown 的标题级别不承载业务数据嵌套深度。

- 只识别文档级 ATX 标题和顶格 fenced code block；不接受列表/引用内的同形块。
- 除区段间空白外，拒绝格式外文本、额外区段和错位块。已接受的完整标题行与围栏块必须覆盖所有非空白源码；检查文首、块间、文尾的源码间隙，不能仅靠 AST 节点白名单断言没有额外内容。正文内部属于围栏块，不做间隙检查。
- 骨架必须是严格 JSON，正文必须有显式闭围栏；CommonMark 可接受未闭合代码块的行为不继承为协议行为。
- 首版只要求接受本文生成规则产生的文档，不承诺手写 Markdown 的所有等价排版或自动修复。
- 检查 Markdig 的源码范围及闭围栏定位实际行为；正文保真以原始输入为依据，不能仅凭库的 roundtrip 宣传认定已验证。

源码覆盖检查只判断未消费区域是否为空白，不解析另一套 Markdown 语法。例如块外的 `[unused]: /url` 不属于本格式，即使 Markdown 解析器没有将它呈现为普通结构节点也必须拒绝；相同文本位于正文围栏内则原样保留。首版块间空白限定为空格、TAB、CR、LF，结构行严格使用 LF。CommonMark 对链接定义的说明见 §10；实际 Markdig 1.3.2 已由运行测试验证。

## 8. 核心性质与后续最小实现切片

对合法值树 V 和合法外置路径集合 P：

```text
Read(Write(V, P)) ≡ V
```

实现时先完成一个 .NET 10.0 类库与一个 xUnit 项目的纵向闭环：JSON DOM → 显式外置 → Markdown → Markdig 定位 → JSON DOM。不先增加 CLR 反射框架、策略系统或持久化设施。

必要验收样例：

1. 用户的 Observation + Recall 示例；选择全部、部分和零个正文。
2. 根字符串、根 null、空键名、`/`、`~`、引号、换行、Unicode 键名和深层结构。
3. 空字符串、各种末尾换行、混合换行、空格、制表符和 U+0000；用逐字符比较验证。
4. 正文含更长围栏、两种围栏、伪造标题；骨架键和值含围栏字符时仍能使用固定 JSON 围栏。
5. 非字符串路径、不存在路径、重复路径/正文、错占位值、重复 JSON 键、缺失闭围栏与多余文本应失败；块外链接定义拒绝，正文内同样文本保留。
6. 原字符串本来等于自身或其他路径；回填不递归解引用；完整删块的已知边界。
7. 大整数、高精度小数、指数数字等不窄化；对象顺序、数组顺序与输入不变；不同路径列表排列只改变正文顺序，不改变读回值。

以上验收类别已落实为 xUnit 测试；Debug 与 Release 均通过 106 项测试，其中一个确定性生成测试包含 128 组正文。独立 PackageReference 消费者也已通过公开接口验证，详细证据见实施记录。LLM 阅读关联准确率和 token 数仍需另用真实样本比较，不作为已证实收益。

## 9. 明确暂缓

自动选择与正则路径、一个正文多处引用、内容去重、跨文档引用、稳定实体 ID、流式读写、容错修复、恢复渲染选项、格式迁移、持久化、通用 Markdown 编辑、LLM 输出格式保证。

首版实施已验证 Markdig 1.3.2 的源码范围、CR/NUL 与额外节点处理，以及 System.Text.Json DOM 的唯一键和数值保真。读写深度配置保持一致，包含 96 层结构回归测试；完整输入仍受进程内存与调用栈资源约束。

## 10. 外部依据

- [RFC 6901 — JSON Pointer](https://www.rfc-editor.org/rfc/rfc6901)：路径语法、求值、特殊键与重复键的定位问题。
- [CommonMark — Fenced code blocks](https://spec.commonmark.org/0.31.2/#fenced-code-blocks)：围栏匹配、缩进及未闭合块行为。
- [CommonMark — Insecure characters](https://spec.commonmark.org/0.31.2/#insecure-characters)：U+0000 规范化说明，支持保留原始输入的必要性。
- [CommonMark — Link reference definitions](https://spec.commonmark.org/0.31.2/#link-reference-definitions)：仅检查可见或普通结构节点不足以独立证明源码没有额外内容。
- [RFC 8259 — JSON](https://www.rfc-editor.org/rfc/rfc8259)：严格 JSON 的字符串与换行语法；固定骨架围栏的依据。
- [Markdig FencedCodeBlock](https://github.com/xoofx/markdig/blob/master/src/Markdig/Syntax/FencedCodeBlock.cs)：开闭围栏长度等节点信息；源码链接指向可变化的开发分支，首版以实际 Markdig 1.3.2 运行测试为验收证据。
- [System.Text.Json DOM](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-dom)：既有 JSON 值树能力。
