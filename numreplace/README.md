# numreplace

AutoCAD 数字文字批量替换插件。

这个插件用于在指定文字图层和框选范围内查找纯数字文字。满足大于、等于、小于或区间条件的数字，可以替换成同一个固定数值，也可以随机替换成指定区间内的数字。

## 命令

| 命令 | 作用 |
| --- | --- |
| `NUMREPLACE` | 按条件批量替换数字文字 |
| `NUMREPLACELOG` | 显示诊断日志路径 |

## 使用流程

1. 在 AutoCAD 命令行输入 `NETLOAD`。
2. 加载 DLL：

```text
numreplace-v0.1.1-autocad2021.dll
```

3. 输入命令：

```text
NUMREPLACE
```

4. 选择目标数值匹配方式：

```text
大于(G) / 等于(E) / 小于(L) / 区间(R)，默认 G
```

5. 输入目标数值。如果选择区间 `R`，先输入目标区间最大值，再输入目标区间最小值。
6. 选择一个文字对象，用于确定文字图层。
7. 确认是否使用该文字图层。
8. 框选需要处理的文字范围。
9. 选择是否保留原文字的小数位数，默认保留。
10. 选择替换方式：

```text
固定值(F) / 区间随机(R)，默认 F
```

11. 如果选择固定值 `F`，输入替换后的固定数值。
12. 如果选择区间随机 `R`，输入替换后的最小值和最大值。
13. 插件会把满足条件的数字文字按选择的方式替换。

## 判断规则

- 支持 `DBText` 和 `MText`。
- 只处理所选文字图层、且在框选范围内的文字对象。
- 只识别纯数字文字，例如 `100`、`100.5`、`-20`、`.5`。
- `100mm`、`梁100`、`A=100`、`100%` 会作为非数字文字跳过。
- `G` 是严格大于，`E` 是等于，`L` 是严格小于，`R` 是包含边界的区间匹配。
- 如果保留原小数位数，`135` 会替换为整数，`135.20` 会替换为两位小数。
- 如果不保留原小数位数，会使用输入替换值的小数位数。

## 日志

如果结果不对，运行：

```text
NUMREPLACELOG
```

日志默认写到：

```text
%TEMP%\NUMREPLACE.log
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
dist\numreplace-v0.1.1-autocad2021.dll
```
