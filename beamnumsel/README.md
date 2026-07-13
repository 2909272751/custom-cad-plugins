# beamnumsel

AutoCAD 梁上数字选择插件。

这个插件用于在指定梁线图层和文字图层中，框选一片区域后自动预选梁线旁边的数字文字。新版使用固定最大识别距离、横竖方向和文字所在侧共同判断，并支持预览确认和调整距离。

## 命令

| 命令 | 作用 |
| --- | --- |
| `BEAMNUMSEL` | 选择梁上数字文字 |
| `BEAMNUMSELLOG` | 显示诊断日志路径 |

## 使用流程

1. 在 AutoCAD 命令行输入 `NETLOAD`。
2. 加载 DLL：

```text
beamnumsel-v0.2.0-autocad2021.dll
```

3. 输入命令：

```text
BEAMNUMSEL
```

4. 选择梁线图层上的一条梁线。
5. 确认是否使用该梁线图层。
6. 选择数字文字图层上的一个文字。
7. 确认是否使用该文字图层。
8. 选择识别方向：

```text
横向(H) / 竖向(V) / 全部(A)，默认 A
```

9. 如果识别横向梁线，选择上方、下方或两侧数字：

```text
上方(U) / 下方(D) / 两侧(B)，默认 U
```

10. 如果识别竖向梁线，选择左侧、右侧或两侧数字：

```text
左侧(L) / 右侧(R) / 两侧(B)，默认 L
```

11. 输入最大识别距离，默认 `50`。
12. 框选需要识别的梁和数字范围。
13. 插件会预览选中的数字。
14. 确认预览结果，或输入 `D` 调整距离重新预览。

## 识别规则

- 支持梁线对象：`Line` 和直线段 `Polyline`。
- 支持文字对象：`DBText` 和 `MText`。
- 只识别纯数字文字，例如 `0.14`、`0.18`、`-0.02`。
- `A=0.14`、`0.14m`、`梁0.14` 会作为非数字文字跳过。
- 横向梁线优先匹配横排数字，竖向梁线优先匹配竖排数字。
- 每个数字只归属到投影重合更好、距离更近的一条梁线。
- v0.2.0 改为预览式流程，可调整最大识别距离后重新计算。

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
dist\beamnumsel-v0.2.0-autocad2021.dll
```
