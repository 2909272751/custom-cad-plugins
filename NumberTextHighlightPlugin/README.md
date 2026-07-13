# NumberTextHighlightPlugin

AutoCAD 数字文字条件标红插件。

这个插件用于在指定文字图层和框选范围内检查文字内容。如果文字内容是纯数字，并且满足指定的大于、等于或小于条件，插件会把该文字改成红色。

## 命令

| 命令 | 作用 |
| --- | --- |
| `NUMRED` | 按数字条件把文字标红 |
| `NUMREDLOG` | 显示诊断日志路径 |

## 使用流程

1. 在 AutoCAD 命令行输入 `NETLOAD`。
2. 加载 DLL：

```text
NumberTextHighlightPlugin-v0.1.0-autocad2021.dll
```

3. 输入命令：

```text
NUMRED
```

4. 输入用于比较的数值。
5. 选择一个文字对象，用于确定文字图层。
6. 确认是否使用该文字图层。
7. 框选需要检查的文字范围。
8. 选择判断条件：

```text
大于(G) / 等于(E) / 小于(L)，默认 G
```

9. 插件会把满足条件的数字文字改成红色。

## 判断规则

- 支持 `DBText` 和 `MText`。
- 只处理所选文字图层、且在框选范围内的文字对象。
- 第一版只识别纯数字文字，例如 `100`、`100.5`、`-20`、`.5`。
- `100mm`、`梁100`、`A=100`、`100%` 会作为非数字文字跳过。
- `G` 是严格大于，`E` 是等于，`L` 是严格小于。

## 日志

如果结果不对，运行：

```text
NUMREDLOG
```

日志默认写到：

```text
%TEMP%\NUMRED.log
```

## 构建

默认按本机 AutoCAD 2021 路径构建：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

指定 AutoCAD 安装目录：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -AcadPath "D:\autocad\AutoCAD 2021" -AcadLabel "autocad2021"
```

输出：

```text
dist\NumberTextHighlightPlugin-v0.1.0-autocad2021.dll
```
