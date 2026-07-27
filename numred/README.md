# numred

AutoCAD 鏁板瓧鏂囧瓧鏉′欢鏍囩孩鎻掍欢銆?
杩欎釜鎻掍欢鐢ㄤ簬鍦ㄦ寚瀹氭枃瀛楀浘灞傚拰妗嗛€夎寖鍥村唴妫€鏌ユ枃瀛楀唴瀹广€傚鏋滄枃瀛楀唴瀹规槸绾暟瀛楋紝骞朵笖婊¤冻鎸囧畾鐨勫ぇ浜庛€佺瓑浜庢垨灏忎簬鏉′欢锛屾彃浠朵細鎶婅鏂囧瓧鏀规垚绾㈣壊銆?
## 鍛戒护

| 鍛戒护 | 浣滅敤 |
| --- | --- |
| `NUMRED` | 鎸夋暟瀛楁潯浠舵妸鏂囧瓧鏍囩孩 |
| `NUMREDLOG` | 鏄剧ず璇婃柇鏃ュ織璺緞 |

## 浣跨敤娴佺▼

1. 涓嬭浇 `numred-v0.1.0-autocad2021.zip` 骞惰В鍘嬨€?2. 杩愯瑙ｅ帇鐩綍閲岀殑 `unblock.ps1`銆?3. 鍦?AutoCAD 鍛戒护琛岃緭鍏?`NETLOAD`銆?4. 鍔犺浇 DLL锛?
```text
numred-v0.1.0-autocad2021.dll
```

3. 杈撳叆鍛戒护锛?
```text
NUMRED
```

4. 杈撳叆鐢ㄤ簬姣旇緝鐨勬暟鍊笺€?5. 閫夋嫨涓€涓枃瀛楀璞★紝鐢ㄤ簬纭畾鏂囧瓧鍥惧眰銆?6. 纭鏄惁浣跨敤璇ユ枃瀛楀浘灞傘€?7. 妗嗛€夐渶瑕佹鏌ョ殑鏂囧瓧鑼冨洿銆?8. 閫夋嫨鍒ゆ柇鏉′欢锛?
```text
澶т簬(G) / 绛変簬(E) / 灏忎簬(L)锛岄粯璁?G
```

9. 鎻掍欢浼氭妸婊¤冻鏉′欢鐨勬暟瀛楁枃瀛楁敼鎴愮孩鑹层€?
## 鍒ゆ柇瑙勫垯

- 鏀寔 `DBText` 鍜?`MText`銆?- 鍙鐞嗘墍閫夋枃瀛楀浘灞傘€佷笖鍦ㄦ閫夎寖鍥村唴鐨勬枃瀛楀璞°€?- 绗竴鐗堝彧璇嗗埆绾暟瀛楁枃瀛楋紝渚嬪 `100`銆乣100.5`銆乣-20`銆乣.5`銆?- `100mm`銆乣姊?00`銆乣A=100`銆乣100%` 浼氫綔涓洪潪鏁板瓧鏂囧瓧璺宠繃銆?- `G` 鏄弗鏍煎ぇ浜庯紝`E` 鏄瓑浜庯紝`L` 鏄弗鏍煎皬浜庛€?
## 鏃ュ織

濡傛灉缁撴灉涓嶅锛岃繍琛岋細

```text
NUMREDLOG
```

鏃ュ織榛樿鍐欏埌锛?
```text
%TEMP%\NUMRED.log
```

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
dist\numred-v0.1.0-autocad2021.dll
```
