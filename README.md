# MdJson

面向 LLM prompt 的 JSON 替代表现形式：保留 JSON 骨架，把显式指定且写成 JSON 时需要转义的字符串放进 Markdown 代码围栏，正文无需 JSON 转义；通过 JSON Pointer 无损回填。

## 使用

目标框架为 .NET 10。引用 `src/MdJson/MdJson.csproj`，使用 System.Text.Json 完成 CLR 对象转换：

```csharp
using System.Text.Json;
using Atelia.MdJson;

var value = JsonSerializer.SerializeToElement(new
{
    observation = new[]
    {
        new { name = "张三", intention = "喝口水，继续说道：\n“我跟你说呀！”" }
    }
});

string markdown = MdJsonSerializer.Write(value, ["/observation/0/intention"]);
JsonElement restored = MdJsonSerializer.Read(markdown);
// 可继续 restored.Deserialize<YourType>(options)。
```

`Write(JsonElement, IReadOnlyList<string>)` 不修改输入。路径列表指定候选字符串：先验证每条路径，再用骨架的 JSON 编码器判断其值；编码后需要转义的值才外置，其余值直接留在骨架中。路径列表为空时，所有值保留在 JSON 骨架中；实际外置的正文按列表顺序输出。空路径 `""` 可选中根字符串。返回的 `JsonElement` 不依赖调用方管理临时 `JsonDocument`。

`Read(string)` 通过 Markdig 识别块，并从原始源码恢复正文。非法格式、路径、占位值或不支持的 JSON 值域抛出 `FormatException`；null 参数抛出 `ArgumentNullException`。正文回填不会递归解释其中的路径或 Markdown。

## 格式边界

- JSON 对象键必须唯一，字符串必须是有效 Unicode；保留属性顺序、数字精度及正文中的 CR、LF、制表符与 NUL。
- 正文围栏按内容自动加长。结构行使用 LF；传输或编辑器必须保留原字符才能保证逐字符往返。
- 只接受约定的 `structure` / `content` 区段和正文块，不是通用 Markdown 导入器。
- 正文标题是唯一回填清单；完整删除一整块正文可能仍得到合法文档，格式不提供这种完整性检测。
- 首版在内存中处理完整值树和文本；不提供自动路径选择、共享引用或流式处理。

详细规则见 [DESIGN.md](DESIGN.md)，实现与验收见 [IMPLEMENTATION.md](IMPLEMENTATION.md)。尚未评估具体 LLM 的理解准确率或 token 收益。

## 构建与测试

安装 .NET SDK 10.0.201（允许同功能带较新补丁）后：

```shell
dotnet restore MdJson.slnx
dotnet build MdJson.slnx -c Release --no-restore
dotnet test MdJson.slnx -c Release --no-build
```

目录：`src/MdJson` 为类库，`tests/MdJson.Tests` 为 xUnit 验收测试。
