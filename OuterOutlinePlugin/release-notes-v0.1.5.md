# OuterOutlinePlugin v0.1.5

## 修复

- 修复同一轮廓场景下可能生成两条闭合线的问题。
- 修复面积相同的候选轮廓中错误保留“内吃线”的问题。
- 当前版本固定只输出一条外轮廓。
- 当候选轮廓面积相同时，优先保留周长更短的轮廓，更贴近实际光滑外表面。

## 文档

- 重写 README，增加面向新手的详细操作步骤。
- 补充图层选择建议、日志说明和排错说明。

## 使用

在 AutoCAD 2022 中运行：

```text
NETLOAD
```

加载：

```text
OuterOutlinePlugin-v0.1.5-autocad2022.dll
```

然后运行：

```text
PCOUTLINE
```

## 产物

- `OuterOutlinePlugin-v0.1.5-autocad2022.dll`
