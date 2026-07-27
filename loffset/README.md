# loffset

AutoCAD 鎵归噺 Offset 鎻掍欢銆?
杩欎釜鎻掍欢鐢ㄤ簬鎸夊浘灞傛壒閲?offset 绾裤€佸姬銆佸娈电嚎銆佸渾銆佹き鍦嗐€佹牱鏉＄瓑 AutoCAD `Curve` 瀵硅薄銆傚畠浼氬厛璁╀綘閫夋嫨闇€瑕佸鐞嗙殑鍥惧眰骞堕珮浜樉绀猴紝鐒跺悗纭鍥惧眰銆佹閫夐渶瑕佸鐞嗙殑鍖哄煙锛屾渶鍚庤緭鍏?offset 璺濈銆佹柟鍚戝拰鏄惁鍒犻櫎鍘熷浘銆?
## 鍛戒护

| 鍛戒护 | 浣滅敤 |
| --- | --- |
| `LOFFSET` | 鎸夊浘灞傚拰妗嗛€夊尯鍩熸壒閲?offset 鏇茬嚎瀵硅薄 |

## 浣跨敤娴佺▼

1. 涓嬭浇 `loffset-v0.1.1-autocad2021.zip` 骞惰В鍘嬨€?2. 杩愯瑙ｅ帇鐩綍閲岀殑 `unblock.ps1`銆?3. 鍦?AutoCAD 鍛戒护琛岃緭鍏?`NETLOAD`銆?4. 鍔犺浇 DLL锛?
```text
loffset-v0.1.1-autocad2021.dll
```

3. 杈撳叆鍛戒护锛?
```text
LOFFSET
```

4. 鐐归€夐渶瑕?offset 鐨勫浘灞備笂鐨勪换鎰忓璞°€?5. 鎻掍欢浼氶珮浜綋鍓嶇┖闂村唴璇ュ浘灞傜殑鎵€鏈夊璞°€?6. 纭鏄惁浣跨敤褰撳墠楂樹寒鍥惧眰锛?
```text
鏄惁纭浣跨敤褰撳墠楂樹寒鐨勫浘灞?[鏄?Y)/鍚?N)] <Y>:
```

7. 妗嗛€?浜ゅ弶妗嗛€夐渶瑕?offset 鐨勫尯鍩熸垨瀵硅薄銆傛彃浠跺彧浼氬鐞嗘閫夊埌鐨勩€佷笖灞炰簬宸茬‘璁ゅ浘灞傜殑瀵硅薄銆?8. 杈撳叆 offset 璺濈銆?9. 杈撳叆鏂瑰悜锛?
```text
鍐?N) / 澶?W)锛岄粯璁?W
```

10. 閫夋嫨鏄惁鍒犻櫎鍘熷浘褰細

```text
鏄?Y) / 鍚?N)锛岄粯璁?N
```

11. 杈撳叆 offset 鍚庡浘褰㈤鑹插彿锛?
```text
1-255锛岄粯璁?1 绾㈣壊
```

## 鏀寔瀵硅薄

- `Polyline`
- `Line`
- `Arc`
- `Circle`
- `Ellipse`
- `Spline`
- 鍏朵粬缁ф壙鑷?AutoCAD `Curve` 涓旀敮鎸?`GetOffsetCurves` 鐨勫璞?
闂悎涓旇兘璁＄畻闈㈢Н鐨勫璞′細鍚屾椂灏濊瘯姝ｈ礋 offset锛屽苟鐢ㄩ潰绉ぇ灏忓垽鏂唴澶栥€傛櫘閫氱嚎娈点€佸姬绾跨瓑寮€鏀炬洸绾挎病鏈夌粷瀵瑰唴澶栨蹇碉紝鎻掍欢涓?`W` 瀵瑰簲姝ｈ窛绂伙紝`N` 瀵瑰簲璐熻窛绂汇€?
## 褰撳墠闄愬埗

- 鍙鐞嗗綋鍓嶆ā鍨?甯冨眬绌洪棿銆?- 涓嶈繘鍏ュ潡鍙傜収鍐呴儴澶勭悊銆?- 鏌愪簺澶嶆潅鏍锋潯銆佽嚜浜ゆ洸绾裤€佸紓甯稿嚑浣曞彲鑳借 AutoCAD API 鎷掔粷 offset锛屽懡浠ょ粨鏉熸椂浼氱粺璁″け璐ユ暟閲忋€?
## 鏋勫缓

榛樿鎸夋湰鏈?AutoCAD 2021 璺緞鏋勫缓锛?
```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

鎸囧畾 AutoCAD 瀹夎鐩綍锛?
```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -AcadPath "D:\autocad\AutoCAD 2021" -AcadLabel "autocad2021"
```

杈撳嚭锛?
```text
dist\loffset-v0.1.1-autocad2021.dll
```
