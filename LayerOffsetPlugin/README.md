# LayerOffsetPlugin

AutoCAD 批量 Offset 插件。

这个插件用于按图层批量 offset 线、弧、多段线、圆、椭圆、样条等 AutoCAD `Curve` 对象。它会先让你选择需要处理的图层并高亮显示，然后确认图层、框选需要处理的区域，最后输入 offset 距离、方向和是否删除原图。

## 命令

| 命令 | 作用 |
| --- | --- |
| `LOFFSET` | 按图层和框选区域批量 offset 曲线对象 |

## 使用流程

1. 下载 `LayerOffsetPlugin-v0.1.1-autocad2021.zip` 并解压。
2. 运行解压目录里的 `unblock.ps1`。
3. 在 AutoCAD 命令行输入 `NETLOAD`。
4. 加载 DLL：

```text
LayerOffsetPlugin-v0.1.1-autocad2021.dll
```

3. 输入命令：

```text
LOFFSET
```

4. 点选需要 offset 的图层上的任意对象。
5. 插件会高亮当前空间内该图层的所有对象。
6. 确认是否使用当前高亮图层：

```text
是否确认使用当前高亮的图层 [是(Y)/否(N)] <Y>:
```

7. 框选/交叉框选需要 offset 的区域或对象。插件只会处理框选到的、且属于已确认图层的对象。
8. 输入 offset 距离。
9. 输入方向：

```text
内(N) / 外(W)，默认 W
```

10. 选择是否删除原图形：

```text
是(Y) / 否(N)，默认 N
```

11. 输入 offset 后图形颜色号：

```text
1-255，默认 1 红色
```

## 支持对象

- `Polyline`
- `Line`
- `Arc`
- `Circle`
- `Ellipse`
- `Spline`
- 其他继承自 AutoCAD `Curve` 且支持 `GetOffsetCurves` 的对象

闭合且能计算面积的对象会同时尝试正负 offset，并用面积大小判断内外。普通线段、弧线等开放曲线没有绝对内外概念，插件中 `W` 对应正距离，`N` 对应负距离。

## 当前限制

- 只处理当前模型/布局空间。
- 不进入块参照内部处理。
- 某些复杂样条、自交曲线、异常几何可能被 AutoCAD API 拒绝 offset，命令结束时会统计失败数量。

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
dist\LayerOffsetPlugin-v0.1.1-autocad2021.dll
```
