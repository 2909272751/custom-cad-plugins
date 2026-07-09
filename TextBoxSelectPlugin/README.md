# TextBoxSelectPlugin

AutoCAD 选择“框内有文字”的封闭 PL 框插件。

这个插件用于从一批封闭 `Polyline` 框里，自动找出内部包含文字的框，并把这些框和对应文字一起放入当前选择集。适合图纸里有很多矩形框、每个框内可能有红色编号或标注文字的场景。

## 命令

| 命令 | 作用 |
| --- | --- |
| `TXTBOXSEL` | 选中框选范围内包含文字的封闭 PL 框 |

## 使用流程

1. 在 AutoCAD 命令行输入 `NETLOAD`。
2. 加载 DLL：

```text
TextBoxSelectPlugin-v0.1.3-autocad2021.dll
```

3. 输入命令：

```text
TXTBOXSEL
```

4. 点选框线图层上的任意对象，插件会高亮该图层对象。
5. 确认是否使用当前高亮的框线图层。
6. 点选文字图层上的任意文字，插件会高亮该图层对象。
7. 确认是否使用当前高亮的文字图层。
8. 框选/交叉框选需要识别的区域。
9. 插件会选中框内有文字的封闭 PL 框，并同时选中对应文字。

## 判断规则

- 框对象：当前版本识别闭合 `Polyline`。
- 文字对象：识别 `DBText` 和 `MText`。
- 判断方式：取文字几何包围盒中心点，判断该点是否落在封闭 PL 框内部。
- 候选框必须包含文字中心点，并且文字几何包围盒需要落在候选框外包范围内。
- 如果一个文字同时落在多个合格的重叠/嵌套框内，只选中面积最小的封闭 PL 框，并把该文字一起选中。
- 带弧段的 PL 框会进行弧段采样后再判断。

## 当前限制

- 不自动把零散 `Line` 拼成闭合框。
- 不进入块参照内部识别。
- 如果文字中心点不在框内，即使文字有一部分压到框内，也不会算作命中。

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
dist\TextBoxSelectPlugin-v0.1.3-autocad2021.dll
```
