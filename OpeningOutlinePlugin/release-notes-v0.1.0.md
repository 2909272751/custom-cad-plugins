# OpeningOutlinePlugin v0.1.0

AutoCAD 2022 .NET 开洞轮廓生成插件初始版本。

## 功能

- `DKTRACE`：识别开洞符号并生成闭合 `LWPOLYLINE`
- `DKLOG`：显示诊断日志路径
- 支持按开洞符号图层自动识别，并通过 `Done/Add/Remove` 修正
- 支持连续点选边界样本图层，实时高亮参与计算的边界对象
- 支持 `LINE`、`LWPOLYLINE` 直线/弧线段、`ARC`、`HATCH` 边界循环
- 失败位置会标记到 `DK_OPENING_FAILED` 图层

## 使用

在 AutoCAD 2022 中运行：

```text
NETLOAD
```

加载：

```text
OpeningOutlinePlugin-v0.1.0-autocad2022.dll
```

然后运行：

```text
DKTRACE
```

## 日志

诊断日志：

```text
%TEMP%\DKTRACE.log
```

## 产物

- `OpeningOutlinePlugin-v0.1.0-autocad2022.dll`
