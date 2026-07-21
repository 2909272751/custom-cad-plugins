# 自建 CAD 插件

> 本项目 100% 由 Codex 生成。

这个仓库是一个 AutoCAD 插件合集，目前包含 8 个彼此独立的插件。

每个插件都有自己的源码、构建脚本、发布 DLL 和 README。你可以把它当成一个总项目管理，也可以单独进入某个插件目录查看说明、编译或加载。

## 插件下载

| 插件 | 命令 | 一句话功能 | 下载 |
| --- | --- | --- | --- |
| 简易说明 | - | 简短说明八个插件分别做什么，以及通用 NETLOAD 加载方法。 | [下载 TXT（自建CAD插件-v0.1.1-简易说明）](https://github.com/2909272751/custom-cad-plugins/releases/download/v0.1.1/custom-cad-plugins-v0.1.1-quick-guide.txt) |
| `OpeningOutlinePlugin` | `DKTRACE` | 根据开洞符号和边界图层，生成洞口闭合轮廓线。 | [下载 DLL](https://github.com/2909272751/custom-cad-plugins/releases/download/v0.1.1/OpeningOutlinePlugin-v0.1.1-autocad2022.dll) |
| `OuterOutlinePlugin` | `PCOUTLINE` | 从楼层或构件边线中提取干净的外轮廓闭合线。 | [下载 DLL](https://github.com/2909272751/custom-cad-plugins/releases/download/v0.1.1/OuterOutlinePlugin-v0.1.5-autocad2022.dll) |
| `HatchOuterPolylinePlugin` | `HATCHPL` | 从 Hatch 直接提取最外层闭合 Polyline，忽略内部小洞和文字洞。 | [下载 DLL](https://github.com/2909272751/custom-cad-plugins/releases/download/v0.1.1/HatchOuterPolylinePlugin-v0.1.11-autocad2022.dll) |
| `LayerOffsetPlugin` | `LOFFSET` | 按图层和框选区域批量 offset 曲线对象，可选择内外方向、是否删除原图和 offset 后颜色。 | [下载 DLL](https://github.com/2909272751/custom-cad-plugins/releases/download/v0.1.1/LayerOffsetPlugin-v0.1.1-autocad2021.dll) |
| `TextBoxSelectPlugin` | `TXTBOXSEL` | 选中框选范围内包含文字的封闭 PL 框，并自动选中对应文字。 | [下载 DLL](https://github.com/2909272751/custom-cad-plugins/releases/download/v0.1.1/TextBoxSelectPlugin-v0.1.5-autocad2021.dll) |
| `NumberTextHighlightPlugin` | `NUMRED` | 按大于、等于、小于条件把选定文字图层中的数字文字标红。 | [下载 DLL](https://github.com/2909272751/custom-cad-plugins/releases/download/v0.1.1/NumberTextHighlightPlugin-v0.1.0-autocad2021.dll) |
| `numreplace` | `NUMREPLACE` | 按条件批量替换选定文字图层中的数字文字，可替换成固定值或区间随机值。 | [下载 DLL](https://github.com/2909272751/custom-cad-plugins/releases/download/v0.1.1/numreplace-v0.1.1-autocad2021.dll) |
| `beamcolor` | `BEAMCOLOR` | 按梁编号前缀批量修改梁编号文字和目标图层线条颜色，跳过配筋文字。 | [下载 DLL](https://github.com/2909272751/custom-cad-plugins/releases/download/v0.1.2/beamcolor-v0.1.2-autocad2021.dll) |

## 新手怎么用

1. 下载需要的 DLL。
2. 打开 AutoCAD。
3. 在命令行输入 `NETLOAD`。
4. 选择刚下载的 DLL。
5. 输入对应插件命令运行。

例如使用 Hatch 外轮廓插件：

```text
NETLOAD
```

选择：

```text
HatchOuterPolylinePlugin-v0.1.11-autocad2022.dll
```

然后运行：

```text
HATCHPL
```

## 详细说明

每个插件目录里都有独立 README：

| 插件 | 说明 |
| --- | --- |
| [`OpeningOutlinePlugin`](./OpeningOutlinePlugin/README.md) | `DKTRACE` 的详细使用步骤、日志和测试方法 |
| [`OuterOutlinePlugin`](./OuterOutlinePlugin/README.md) | `PCOUTLINE` 的详细使用步骤、图层选择和外轮廓生成说明 |
| [`HatchOuterPolylinePlugin`](./HatchOuterPolylinePlugin/README.md) | `HATCHPL` 的详细使用步骤、Hatch 样本选择和小洞补齐说明 |
| [`LayerOffsetPlugin`](./LayerOffsetPlugin/README.md) | `LOFFSET` 的详细使用步骤、图层确认、框选范围和 offset 说明 |
| [`TextBoxSelectPlugin`](./TextBoxSelectPlugin/README.md) | `TXTBOXSEL` 的详细使用步骤、框线图层、文字图层和识别规则说明 |
| [`NumberTextHighlightPlugin`](./NumberTextHighlightPlugin/README.md) | `NUMRED` 的详细使用步骤、数字识别规则和日志说明 |
| [`numreplace`](./numreplace/README.md) | `NUMREPLACE` 的详细使用步骤、区间匹配、固定值/区间随机替换规则和日志说明 |
| [`beamcolor`](./beamcolor/README.md) | `BEAMCOLOR` 的详细使用步骤、梁编号匹配规则、预览确认和日志说明 |

## 目录结构

```text
自建cad插件/
├─ README.md
├─ build-all.ps1
├─ OpeningOutlinePlugin/
│  ├─ README.md
│  ├─ OpeningOutlinePlugin.cs
│  ├─ build.ps1
│  └─ dist/
├─ OuterOutlinePlugin/
│  ├─ README.md
│  ├─ OuterOutlinePlugin.cs
│  ├─ build.ps1
│  └─ dist/
├─ HatchOuterPolylinePlugin/
   ├─ README.md
   ├─ HatchOuterPolylinePlugin.cs
   ├─ build.ps1
   └─ dist/
├─ LayerOffsetPlugin/
   ├─ README.md
   ├─ LayerOffsetPlugin.cs
   ├─ build.ps1
   └─ dist/
├─ TextBoxSelectPlugin/
   ├─ README.md
   ├─ TextBoxSelectPlugin.cs
   ├─ build.ps1
   └─ dist/
├─ NumberTextHighlightPlugin/
   ├─ README.md
   ├─ NumberTextHighlightPlugin.cs
   ├─ build.ps1
   └─ dist/
└─ numreplace/
   ├─ README.md
   ├─ numreplace.cs
   ├─ build.ps1
   └─ dist/
└─ beamcolor/
   ├─ README.md
   ├─ beamcolor.cs
   ├─ build.ps1
   └─ dist/
```

## 统一编译

默认按 AutoCAD 2022 编译：

```powershell
powershell -ExecutionPolicy Bypass -File .\build-all.ps1
```

如果要指定 AutoCAD 安装目录：

```powershell
powershell -ExecutionPolicy Bypass -File .\build-all.ps1 -AcadPath "C:\Program Files\Autodesk\AutoCAD 2022" -AcadLabel "autocad2022"
```

每个插件也可以单独编译：

```powershell
cd .\HatchOuterPolylinePlugin
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

## 加载 DLL 注意事项

AutoCAD 的 `.NET` 插件加载后通常不能真正卸载。如果你加载了旧 DLL，又重新编译了新 DLL，建议重启 AutoCAD 后再 `NETLOAD` 新版本。

如果从浏览器下载 DLL 后 `NETLOAD` 报“不支持操作”或 `FileLoadException`，先在文件属性里点“解除锁定”，或者用 PowerShell：

```powershell
Unblock-File "插件DLL完整路径"
```

## 日志

每个插件都有日志命令，出问题时先运行日志命令查看日志路径。

| 插件 | 日志命令 | 默认日志 |
| --- | --- | --- |
| `OpeningOutlinePlugin` | `DKLOG` | `%TEMP%\DKTRACE.log` |
| `OuterOutlinePlugin` | `PCLOG` | `%TEMP%\PCOUTLINE.log` |
| `HatchOuterPolylinePlugin` | `HATCHPLLOG` | `%TEMP%\HATCHPL.log` |
| `TextBoxSelectPlugin` | `TXTBOXLOG` | `%TEMP%\TXTBOXSEL.log` |
| `NumberTextHighlightPlugin` | `NUMREDLOG` | `%TEMP%\NUMRED.log` |
| `numreplace` | `NUMREPLACELOG` | `%TEMP%\NUMREPLACE.log` |
| `beamcolor` | `BEAMCOLORLOG` | `%TEMP%\BEAMCOLOR.log` |

## Release

当前合集版本是 `v0.1.2`，包含：

- `custom-cad-plugins-v0.1.1-quick-guide.txt`
- `OpeningOutlinePlugin-v0.1.1-autocad2022.dll`
- `OuterOutlinePlugin-v0.1.5-autocad2022.dll`
- `HatchOuterPolylinePlugin-v0.1.11-autocad2022.dll`
- `LayerOffsetPlugin-v0.1.1-autocad2021.dll`
- `TextBoxSelectPlugin-v0.1.5-autocad2021.dll`
- `NumberTextHighlightPlugin-v0.1.0-autocad2021.dll`
- `numreplace-v0.1.1-autocad2021.dll`
- `beamcolor-v0.1.2-autocad2021.dll`
