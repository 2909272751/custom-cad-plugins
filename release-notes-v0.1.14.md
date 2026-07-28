# custom-cad-plugins v0.1.14

本次发布修复 `hatchpl` 到 `v0.1.12`。

## 修复

- `hatchpl-v0.1.12-autocad2022.zip`
  - 命令：`HATCHPL`
  - 修复小尺寸矩形 Hatch 外轮廓被“小凹口清理”误删，最终只生成一根线的问题。
  - 清理小凹口前新增安全校验：结果少于 3 个顶点或面积缩水过大时自动跳过清理。
  - 重新编译并重新打包 release ZIP。

## 继续包含

- `dktrace-v0.1.1-autocad2022.zip`
- `pcoutline-v0.1.5-autocad2022.zip`
- `loffset-v0.1.1-autocad2021.zip`
- `txtboxsel-v0.1.5-autocad2021.zip`
- `numred-v0.1.0-autocad2021.zip`
- `numreplace-v0.1.1-autocad2021.zip`
- `beamcolor-v0.1.8-autocad2021.zip`
- `xrefpick-v0.1.7-autocad2021.zip`
- `lbcp-v0.1.1-autocad2021.zip`
- `custom-cad-plugins-v0.1.14-quick-guide.txt`
