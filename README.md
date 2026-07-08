# 自建 CAD 插件

这个仓库是一个 AutoCAD 插件合集，目前包含 3 个彼此独立的插件。

每个插件都有自己的源码、构建脚本、发布 DLL 和 README。你可以把它当成一个总项目管理，也可以单独进入某个插件目录查看说明、编译或加载。

## 插件列表

| 插件目录 | 命令 | 用途 |
| --- | --- | --- |
| `OpeningOutlinePlugin` | `DKTRACE` | 根据开洞符号和边界图层生成洞口闭合轮廓 |
| `OuterOutlinePlugin` | `PCOUTLINE` | 从楼层/构件边线中提取外轮廓闭合线 |
| `HatchOuterPolylinePlugin` | `HATCHPL` | 从 Hatch 直接提取最外层闭合 Polyline，忽略内部小洞和文字洞 |

## 新手怎么用

1. 进入对应插件目录。
2. 打开该目录里的 `README.md`。
3. 在 AutoCAD 命令行输入 `NETLOAD`。
4. 选择对应插件 `dist` 目录里的 DLL。
5. 输入插件命令运行。

例如使用 Hatch 外轮廓插件：

```text
NETLOAD
```

选择：

```text
HatchOuterPolylinePlugin\dist\HatchOuterPolylinePlugin-v0.1.11-autocad2022.dll
```

然后运行：

```text
HATCHPL
```

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
└─ HatchOuterPolylinePlugin/
   ├─ README.md
   ├─ HatchOuterPolylinePlugin.cs
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

## 发布文件

当前仓库里保留了各插件 `dist` 目录，方便直接下载 DLL 测试。正式发布时建议把 DLL 同时上传到 GitHub Release。
