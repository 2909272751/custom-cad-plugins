# beamnumsel

AutoCAD 梁上数字选择插件。

这个插件用于在指定梁线图层和文字图层中，框选一片区域后自动选中梁线旁边的数字文字：水平梁线选上方数字，竖向梁线选左边数字。

## 命令

| 命令 | 作用 |
| --- | --- |
| `BEAMNUMSEL` | 选择梁上数字文字 |
| `BEAMNUMSELLOG` | 显示诊断日志路径 |

## 使用流程

1. 在 AutoCAD 命令行输入 `NETLOAD`。
2. 加载 DLL：

```text
beamnumsel-v0.1.0-autocad2021.dll
```

3. 输入命令：

```text
BEAMNUMSEL
```

4. 选择一条梁线，用于确定梁线图层。
5. 确认是否使用该梁线图层。
6. 选择一个数字文字，用于确定文字图层。
7. 确认是否使用该文字图层。
8. 框选需要识别的梁和数字范围。
9. 插件会选中满足条件的数字文字。

## 识别规则

- 支持梁线对象：`Line` 和直线段 `Polyline`。
- 支持文字对象：`DBText` 和 `MText`。
- 只识别纯数字文字，例如 `0.14`、`0.18`、`-0.02`。
- `A=0.14`、`0.14m`、`梁0.14` 会作为非数字文字跳过。
- 水平梁线只匹配线条上方、且贴近线条的数字。
- 竖向梁线只匹配线条左侧、且贴近线条的数字。

## 日志

如果结果不对，运行：

```text
BEAMNUMSELLOG
```

日志默认写到：

```text
%TEMP%\BEAMNUMSEL.log
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
dist\beamnumsel-v0.1.0-autocad2021.dll
```
