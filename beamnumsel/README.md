# beamnumsel

AutoCAD 梁上数字选择插件。

这个插件用于在指定梁线图层和文字图层中，框选一片区域后自动选中梁线旁边的数字文字。横向梁线可以选择上方或下方数字，竖向梁线可以选择左侧或右侧数字。

## 命令

| 命令 | 作用 |
| --- | --- |
| `BEAMNUMSEL` | 选择梁上数字文字 |
| `BEAMNUMSELLOG` | 显示诊断日志路径 |

## 使用流程

1. 在 AutoCAD 命令行输入 `NETLOAD`。
2. 加载 DLL：

```text
beamnumsel-v0.1.2-autocad2021.dll
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
9. 选择横向梁线要选哪一侧的数字：

```text
上方(U) / 下方(D)，默认 U
```

10. 选择竖向梁线要选哪一侧的数字：

```text
左侧(L) / 右侧(R)，默认 L
```

11. 插件会选中满足条件的数字文字。

## 识别规则

- 支持梁线对象：`Line` 和直线段 `Polyline`。
- 支持文字对象：`DBText` 和 `MText`。
- 只识别纯数字文字，例如 `0.14`、`0.18`、`-0.02`。
- `A=0.14`、`0.14m`、`梁0.14` 会作为非数字文字跳过。
- 横向梁线按用户选择匹配上方或下方数字。
- 竖向梁线按用户选择匹配左侧或右侧数字。
- v0.1.1 收紧了贴近距离判断，不再按梁线长度放大距离，减少远处数字误选。
- v0.1.2 增加文字方向判断：横向梁线只匹配横排数字，竖向梁线只匹配竖排数字，并适当放宽同方向贴近距离，减少漏选。

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
dist\beamnumsel-v0.1.2-autocad2021.dll
```
