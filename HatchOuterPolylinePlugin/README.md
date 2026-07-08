# HatchOuterPolylinePlugin

AutoCAD Hatch 最外层边界提取插件。

这个插件用于从 `Hatch` 直接生成最外层闭合 `Polyline`。它不调用 `HATCHGENERATEBOUNDARY`，而是直接读取 AutoCAD .NET API 里的 `HatchLoop` 数据，所以可以忽略文字背景遮罩、Island、小洞口等内部 Loop。

## 适合解决什么问题

很多 Hatch 因为文字背景遮罩或内部 Island，会有很多小洞。AutoCAD 自带的边界生成容易把这些洞也生成出来，后续统计面积时还要手动删除。

`HatchOuterPolylinePlugin` 的目标是：

- 只生成 Hatch 的最外层闭合轮廓；
- 忽略内部文字洞、Island、小洞口；
- 保留原 Hatch，不删除、不修改；
- 在当前图层生成闭合 `Polyline`；
- 对小于 600 的小凹口做自动补齐，尽量得到干净的外边界。

## 命令

| 命令 | 作用 |
| --- | --- |
| `HATCHPL` | 提取 Hatch 最外层闭合 Polyline |
| `HATCHPLLOG` | 显示诊断日志路径 |

## 安装 / 加载

在 AutoCAD 命令行输入：

```text
NETLOAD
```

选择 DLL：

```text
dist\HatchOuterPolylinePlugin-v0.1.11-autocad2022.dll
```

加载后运行：

```text
HATCHPL
```

## 操作流程

### 1. 选择要处理的 Hatch 样本

命令行会提示：

```text
第一步：选择要处理的 Hatch 样本
```

点选一个或多个你想处理的 Hatch。插件会按 `图层 + 填充图案名` 记录目标类型。

### 2. 框选要处理的 Hatch 范围

命令行会提示：

```text
第二步：框选要处理的 Hatch 范围
```

你可以框选一大片。插件只处理和第一步样本同类型的 Hatch，其他 Hatch 会自动跳过。

### 3. 查看结果

插件会在当前图层生成闭合 Polyline，原 Hatch 不会被修改。

如果生成结果不对，运行：

```text
HATCHPLLOG
```

日志默认在：

```text
%TEMP%\HATCHPL.log
```

## 当前规则

- 不调用 `HATCHGENERATEBOUNDARY`；
- 只读取 `HatchLoop`；
- 同一个 Hatch 里如果一个外圈包着另一个，只保留外面的；
- 如果两个外圈互不包含，就都描出来；
- 忽略 `Textbox` 类型 Loop；
- 面积很小的内部 Loop 默认忽略；
- 小于 600 的小洞口、小凹口默认拉直补齐；
- 遇到弧线时会按分段折线近似。

## 构建

默认按 AutoCAD 2022 编译：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

指定 AutoCAD 版本：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -AcadPath "C:\Program Files\Autodesk\AutoCAD 2022" -AcadLabel "autocad2022"
```

构建产物：

```text
dist\HatchOuterPolylinePlugin-v0.1.11-autocad2022.dll
```
