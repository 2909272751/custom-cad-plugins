# beamcolor

AutoCAD 梁编号和梁线批量改色插件。

这个插件用于按梁编号前缀规则批量修改梁编号文字和对应梁线颜色。颜色来自用户选择的样本梁编号文字；只识别梁编号这一行，不修改下面的截面尺寸、箍筋、纵筋等配筋文字。

## 命令

| 命令 | 作用 |
| --- | --- |
| `BEAMCOLOR` | 按梁编号前缀批量修改梁编号和梁线颜色 |
| `BEAMCOLORLOG` | 显示诊断日志路径 |

## 使用流程

1. 在 AutoCAD 命令行输入 `NETLOAD`。
2. 加载 DLL：

```text
beamcolor-v0.1.0-autocad2021.dll
```

3. 输入命令：

```text
BEAMCOLOR
```

4. 选择一个梁编号文字，用于确定文字图层和目标颜色。
5. 选择一条对应梁线，用于确定梁线图层。
6. 输入匹配编号前缀规则，例如 `L`、`KL`、`LLK`。
7. 选择是否继续添加更多规则。
8. 框选需要处理的范围。
9. 插件高亮预览将要修改的梁编号和梁线。
10. 确认后正式修改颜色。

## 匹配规则

- 输入 `L` 时，只匹配 `L2(1)`、`L34(1)` 这种 `L + 数字`。
- `LLK` 不会被 `L` 误匹配；如果要处理 `LLK`，请单独添加规则 `LLK`。
- 输入 `KL` 时，匹配 `KL13(1)`、`KL38(2)`。
- 只识别梁编号格式：英文字母 + 数字 + 可选括号，例如 `KL13(1)`。
- `250x500`、`Φ8@100`、`2Φ18;4Φ20` 等配筋文字会跳过。

## 日志

如果结果不对，运行：

```text
BEAMCOLORLOG
```

日志默认写到：

```text
%TEMP%\BEAMCOLOR.log
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
dist\beamcolor-v0.1.0-autocad2021.dll
```
