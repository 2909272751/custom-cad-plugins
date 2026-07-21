# custom-cad-plugins v0.1.9

- 将所有插件下载资产从直接 DLL 改为 ZIP 包。
- 每个 ZIP 包内包含 DLL、`README.txt` 和 `unblock.ps1`。
- 更新简易说明，统一改为 ZIP 解压后加载 DLL。
- 目标是减少浏览器直接拦截 DLL 下载，并解决下载后 AutoCAD 因网络来源标记无法加载的问题。
