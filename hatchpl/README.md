# hatchpl

AutoCAD Hatch 鏈€澶栧眰杈圭晫鎻愬彇鎻掍欢銆?

杩欎釜鎻掍欢鐢ㄤ簬浠?`Hatch` 鐩存帴鐢熸垚鏈€澶栧眰闂悎 `Polyline`銆傚畠涓嶈皟鐢?`HATCHGENERATEBOUNDARY`锛岃€屾槸鐩存帴璇诲彇 AutoCAD .NET API 閲岀殑 `HatchLoop` 鏁版嵁锛屾墍浠ュ彲浠ュ拷鐣ユ枃瀛楄儗鏅伄缃┿€両sland銆佸皬娲炲彛绛夊唴閮?Loop銆?

## 閫傚悎瑙ｅ喅浠€涔堥棶棰?

寰堝 Hatch 鍥犱负鏂囧瓧鑳屾櫙閬僵鎴栧唴閮?Island锛屼細鏈夊緢澶氬皬娲炪€侫utoCAD 鑷甫鐨勮竟鐣岀敓鎴愬鏄撴妸杩欎簺娲炰篃鐢熸垚鍑烘潵锛屽悗缁粺璁￠潰绉椂杩樿鎵嬪姩鍒犻櫎銆?

`hatchpl` 鐨勭洰鏍囨槸锛?

- 鍙敓鎴?Hatch 鐨勬渶澶栧眰闂悎杞粨锛?
- 蹇界暐鍐呴儴鏂囧瓧娲炪€両sland銆佸皬娲炲彛锛?
- 淇濈暀鍘?Hatch锛屼笉鍒犻櫎銆佷笉淇敼锛?
- 鍦ㄥ綋鍓嶅浘灞傜敓鎴愰棴鍚?`Polyline`锛?
- 瀵瑰皬浜?600 鐨勫皬鍑瑰彛鍋氳嚜鍔ㄨˉ榻愶紝灏介噺寰楀埌骞插噣鐨勫杈圭晫銆?

## 鍛戒护

| 鍛戒护 | 浣滅敤 |
| --- | --- |
| `HATCHPL` | 鎻愬彇 Hatch 鏈€澶栧眰闂悎 Polyline |
| `HATCHPLLOG` | 鏄剧ず璇婃柇鏃ュ織璺緞 |

## 瀹夎 / 鍔犺浇

鍦?AutoCAD 鍛戒护琛岃緭鍏ワ細

```text
NETLOAD
```

閫夋嫨 DLL锛?

```text
dist\hatchpl-v0.1.11-autocad2022.dll
```

鍔犺浇鍚庤繍琛岋細

```text
HATCHPL
```

## 鎿嶄綔娴佺▼

### 1. 閫夋嫨瑕佸鐞嗙殑 Hatch 鏍锋湰

鍛戒护琛屼細鎻愮ず锛?

```text
绗竴姝ワ細閫夋嫨瑕佸鐞嗙殑 Hatch 鏍锋湰
```

鐐归€変竴涓垨澶氫釜浣犳兂澶勭悊鐨?Hatch銆傛彃浠朵細鎸?`鍥惧眰 + 濉厖鍥炬鍚峘 璁板綍鐩爣绫诲瀷銆?

### 2. 妗嗛€夎澶勭悊鐨?Hatch 鑼冨洿

鍛戒护琛屼細鎻愮ず锛?

```text
绗簩姝ワ細妗嗛€夎澶勭悊鐨?Hatch 鑼冨洿
```

浣犲彲浠ユ閫変竴澶х墖銆傛彃浠跺彧澶勭悊鍜岀涓€姝ユ牱鏈悓绫诲瀷鐨?Hatch锛屽叾浠?Hatch 浼氳嚜鍔ㄨ烦杩囥€?

### 3. 鏌ョ湅缁撴灉

鎻掍欢浼氬湪褰撳墠鍥惧眰鐢熸垚闂悎 Polyline锛屽師 Hatch 涓嶄細琚慨鏀广€?

濡傛灉鐢熸垚缁撴灉涓嶅锛岃繍琛岋細

```text
HATCHPLLOG
```

鏃ュ織榛樿鍦細

```text
%TEMP%\HATCHPL.log
```

## 褰撳墠瑙勫垯

- 涓嶈皟鐢?`HATCHGENERATEBOUNDARY`锛?
- 鍙鍙?`HatchLoop`锛?
- 鍚屼竴涓?Hatch 閲屽鏋滀竴涓鍦堝寘鐫€鍙︿竴涓紝鍙繚鐣欏闈㈢殑锛?
- 濡傛灉涓や釜澶栧湀浜掍笉鍖呭惈锛屽氨閮芥弿鍑烘潵锛?
- 蹇界暐 `Textbox` 绫诲瀷 Loop锛?
- 闈㈢Н寰堝皬鐨勫唴閮?Loop 榛樿蹇界暐锛?
- 灏忎簬 600 鐨勫皬娲炲彛銆佸皬鍑瑰彛榛樿鎷夌洿琛ラ綈锛?
- 閬囧埌寮х嚎鏃朵細鎸夊垎娈垫姌绾胯繎浼笺€?

## 鏋勫缓

榛樿鎸?AutoCAD 2022 缂栬瘧锛?

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

鎸囧畾 AutoCAD 鐗堟湰锛?

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -AcadPath "C:\Program Files\Autodesk\AutoCAD 2022" -AcadLabel "autocad2022"
```

鏋勫缓浜х墿锛?

```text
dist\hatchpl-v0.1.11-autocad2022.dll
```
