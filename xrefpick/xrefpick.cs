using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace xrefpick
{
    public class xrefpickCommand : IExtensionApplication
    {
        private const string BackupDictionaryName = "XREFPICK_COLOR_BACKUP";
        private const string FillLayerBackupDictionaryName = "XREFPICK_FILL_LAYER_BACKUP";
        private const string FillObjectBackupDictionaryName = "XREFPICK_FILL_OBJECT_BACKUP";
        private const int DefaultXrefColorIndex = 35;
        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "XREFPICK.log");
        private static readonly string ConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "custom-cad-plugins");
        private static readonly string ConfigPath = Path.Combine(ConfigDir, "xrefpick.cfg");
        private static readonly string PickautoBackupPath = Path.Combine(ConfigDir, "xrefpick_pickauto.cfg");
        private static readonly string PickdragBackupPath = Path.Combine(ConfigDir, "xrefpick_pickdrag.cfg");
        private static readonly string PickstyleBackupPath = Path.Combine(ConfigDir, "xrefpick_pickstyle.cfg");
        private static readonly HashSet<Document> HookedDocs = new HashSet<Document>();
        private static bool enabled;
        private static DateTime lastPointMonitor = DateTime.MinValue;

        public void Initialize()
        {
            enabled = LoadEnabled();
            HookDocumentEvents();
            if (enabled)
            {
                ApplyWindowSelectionAssist(null);
            }

            DocumentCollection docs = Application.DocumentManager;
            if (docs != null)
            {
                docs.DocumentCreated += OnDocumentCreated;
                docs.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
            }

            WriteLog("Initialized. enabled=" + enabled);
        }

        public void Terminate()
        {
            try
            {
                DocumentCollection docs = Application.DocumentManager;
                if (docs != null)
                {
                    docs.DocumentCreated -= OnDocumentCreated;
                    docs.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
                }

                foreach (Document doc in new List<Document>(HookedDocs))
                {
                    UnhookDocument(doc);
                }
            }
            catch
            {
            }
        }

        [CommandMethod("XREFPICK")]
        public void RunXrefPick()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            HookDocument(doc);
            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                XrefColorBackupStatus backupStatus = GetColorBackupStatus(db);
                WriteStatus(ed, backupStatus);

                PromptKeywordOptions actionOptions = new PromptKeywordOptions("\n请选择操作 [切换过滤(F)/隐藏填充(H)/恢复填充(U)/外参改色(C)/恢复颜色(R)/状态(S)] <F>: ");
                actionOptions.AllowNone = true;
                actionOptions.Keywords.Add("F");
                actionOptions.Keywords.Add("H");
                actionOptions.Keywords.Add("U");
                actionOptions.Keywords.Add("C");
                actionOptions.Keywords.Add("R");
                actionOptions.Keywords.Add("S");
                actionOptions.Keywords.Default = "F";

                PromptResult actionResult = ed.GetKeywords(actionOptions);
                if (actionResult.Status == PromptStatus.Cancel)
                {
                    WriteLog("XREFPICK cancelled at action prompt.");
                    return;
                }

                string action = NormalizeKeyword(actionResult, "F");
                if (action == "F")
                {
                    ToggleFilter(doc);
                }
                else if (action == "H")
                {
                    HideScopedFill(doc);
                }
                else if (action == "U")
                {
                    RestoreScopedFill(doc);
                }
                else if (action == "C")
                {
                    ApplyXrefLayerColor(doc);
                }
                else if (action == "R")
                {
                    RestoreXrefLayerColors(doc);
                }
                else
                {
                    WriteStatus(ed, GetColorBackupStatus(db));
                }
            }
            catch (System.Exception ex)
            {
                WriteLog("ERROR: " + ex);
                ed.WriteMessage("\nXREFPICK 执行失败：" + ex.Message);
            }
        }

        [CommandMethod("XREFPICKLOG")]
        public void ShowLogPath()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            doc.Editor.WriteMessage("\nXREFPICK 日志路径: " + LogPath);
            doc.Editor.WriteMessage("\nXREFPICK 配置路径: " + ConfigPath);
        }

        private static void ToggleFilter(Document doc)
        {
            Editor ed = doc.Editor;
            ed.WriteMessage("\n当前选择过滤：{0}", enabled ? "已开启" : "已关闭");

            if (!PromptYesNo(ed, "\n是否切换状态 [是(Y)/否(N)] <Y>: ", true))
            {
                ed.WriteMessage("\n状态未修改。");
                WriteLog("Toggle filter skipped. enabled=" + enabled);
                return;
            }

            enabled = !enabled;
            SaveEnabled(enabled);
            HookDocumentEvents();

            int removed = 0;
            if (enabled)
            {
                ApplyWindowSelectionAssist(ed);
                removed = RemoveXrefsFromCurrentSelection(doc, true);
            }
            else
            {
                RestoreWindowSelectionAssist(ed);
            }

            ed.WriteMessage("\n外部参照选择过滤已{0}。", enabled ? "开启" : "关闭");
            if (enabled && removed > 0)
            {
                ed.WriteMessage("\n已从当前选择集中移除外部参照 {0} 个。", removed);
            }

            WriteLog("Filter toggled. enabled=" + enabled + ", removedFromCurrentSelection=" + removed);
        }

        private static void HideScopedFill(Document doc)
        {
            Editor ed = doc.Editor;
            int xrefLayerCount = 0;
            int lockedBlockFillCount = 0;
            int skippedObjectCount = 0;

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                Dictionary<string, LayerStateRecord> layerBackup = ReadLayerStateBackup(tr, doc.Database, FillLayerBackupDictionaryName);
                Dictionary<string, ObjectVisibilityRecord> objectBackup = ReadObjectVisibilityBackup(tr, doc.Database, FillObjectBackupDictionaryName);

                xrefLayerCount = HideXrefFillLayers(tr, doc.Database, layerBackup);
                lockedBlockFillCount = HideLockedBlockFillObjects(tr, doc.Database, objectBackup, out skippedObjectCount);

                WriteLayerStateBackup(tr, doc.Database, FillLayerBackupDictionaryName, layerBackup);
                WriteObjectVisibilityBackup(tr, doc.Database, FillObjectBackupDictionaryName, objectBackup);
                tr.Commit();
            }

            ed.Regen();
            ed.WriteMessage("\n完成：已隐藏外参填充图层 {0} 个，锁定块内填充对象 {1} 个，跳过 {2} 个。可用 U 恢复。", xrefLayerCount, lockedBlockFillCount, skippedObjectCount);
            WriteLog("Scoped fill hidden. xrefLayers=" + xrefLayerCount + ", lockedBlockFills=" + lockedBlockFillCount + ", skipped=" + skippedObjectCount);
        }

        private static void RestoreScopedFill(Document doc)
        {
            Editor ed = doc.Editor;
            int restoredLayers = 0;
            int restoredObjects = 0;
            int missingObjects = 0;

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                restoredLayers = RestoreLayerStates(tr, doc.Database, FillLayerBackupDictionaryName);
                RestoreObjectVisibilityResult objectResult = RestoreObjectVisibility(tr, doc.Database, FillObjectBackupDictionaryName);
                restoredObjects = objectResult.Restored;
                missingObjects = objectResult.Missing;
                tr.Commit();
            }

            ed.Regen();
            ed.WriteMessage("\n完成：已恢复填充图层 {0} 个，填充对象 {1} 个，缺失对象 {2} 个。", restoredLayers, restoredObjects, missingObjects);
            WriteLog("Scoped fill restored. layers=" + restoredLayers + ", objects=" + restoredObjects + ", missingObjects=" + missingObjects);
        }

        private static int HideXrefFillLayers(Transaction tr, Database db, Dictionary<string, LayerStateRecord> backup)
        {
            HashSet<string> layerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            LayerTable layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

            foreach (ObjectId btrId in blockTable)
            {
                BlockTableRecord btr = tr.GetObject(btrId, OpenMode.ForRead) as BlockTableRecord;
                if (btr == null || (!btr.IsFromExternalReference && !btr.IsFromOverlayReference))
                {
                    continue;
                }

                foreach (ObjectId id in btr)
                {
                    Entity entity = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                    if (entity == null || !IsFillEntity(entity))
                    {
                        continue;
                    }

                    AddExistingLayerName(layerTable, layerNames, entity.Layer);
                    AddExistingLayerName(layerTable, layerNames, btr.Name + "|" + entity.Layer);
                }
            }

            foreach (ObjectId layerId in layerTable)
            {
                LayerTableRecord layer = tr.GetObject(layerId, OpenMode.ForRead) as LayerTableRecord;
                if (layer == null || !IsXrefDependentLayer(layer))
                {
                    continue;
                }

                if (LooksLikeFillLayer(layer.Name))
                {
                    layerNames.Add(layer.Name);
                }
            }

            int changed = 0;
            foreach (string layerName in layerNames)
            {
                if (!layerTable.Has(layerName))
                {
                    continue;
                }

                LayerTableRecord layer = (LayerTableRecord)tr.GetObject(layerTable[layerName], OpenMode.ForRead);
                if (!backup.ContainsKey(layer.Name))
                {
                    backup[layer.Name] = LayerStateRecord.FromLayer(layer);
                }

                if (!layer.IsOff)
                {
                    layer.UpgradeOpen();
                    layer.IsOff = true;
                    changed++;
                }
            }

            return changed;
        }

        private static int HideLockedBlockFillObjects(Transaction tr, Database db, Dictionary<string, ObjectVisibilityRecord> backup, out int skipped)
        {
            skipped = 0;
            HashSet<ObjectId> lockedBlockDefinitions = new HashSet<ObjectId>();
            HashSet<ObjectId> unlockedBlockDefinitions = new HashSet<ObjectId>();
            HashSet<ObjectId> visitedBlockDefinitions = new HashSet<ObjectId>();
            int changed = 0;

            BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId id in currentSpace)
            {
                BlockReference blockRef = tr.GetObject(id, OpenMode.ForRead, false) as BlockReference;
                if (blockRef == null || IsXrefBlockTableRecord(tr, blockRef.BlockTableRecord))
                {
                    continue;
                }

                if (IsEntityOnLockedLayer(tr, blockRef))
                {
                    lockedBlockDefinitions.Add(blockRef.BlockTableRecord);
                }
                else
                {
                    unlockedBlockDefinitions.Add(blockRef.BlockTableRecord);
                }
            }

            foreach (ObjectId blockTableRecordId in lockedBlockDefinitions)
            {
                if (unlockedBlockDefinitions.Contains(blockTableRecordId))
                {
                    skipped++;
                    WriteLog("Skipped locked block fill hide because same block definition has unlocked references: " + blockTableRecordId);
                    continue;
                }

                changed += HideFillInBlockDefinition(tr, blockTableRecordId, visitedBlockDefinitions, backup, ref skipped);
            }

            return changed;
        }

        private static int HideFillInBlockDefinition(Transaction tr, ObjectId blockTableRecordId, HashSet<ObjectId> visited, Dictionary<string, ObjectVisibilityRecord> backup, ref int skipped)
        {
            if (blockTableRecordId.IsNull || blockTableRecordId.IsErased || visited.Contains(blockTableRecordId))
            {
                return 0;
            }

            visited.Add(blockTableRecordId);
            BlockTableRecord btr = tr.GetObject(blockTableRecordId, OpenMode.ForRead, false) as BlockTableRecord;
            if (btr == null || btr.IsFromExternalReference || btr.IsFromOverlayReference)
            {
                return 0;
            }

            int changed = 0;
            foreach (ObjectId id in btr)
            {
                Entity entity = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (entity == null)
                {
                    skipped++;
                    continue;
                }

                BlockReference nestedBlock = entity as BlockReference;
                if (nestedBlock != null)
                {
                    changed += HideFillInBlockDefinition(tr, nestedBlock.BlockTableRecord, visited, backup, ref skipped);
                    continue;
                }

                if (!IsFillEntity(entity))
                {
                    continue;
                }

                string handle = entity.Handle.ToString();
                if (!backup.ContainsKey(handle))
                {
                    backup[handle] = ObjectVisibilityRecord.FromEntity(entity);
                }

                if (entity.Visible)
                {
                    entity.UpgradeOpen();
                    entity.Visible = false;
                    changed++;
                }
            }

            return changed;
        }

        private static bool IsFillEntity(Entity entity)
        {
            return entity is Hatch || entity is Solid;
        }

        private static bool IsEntityOnLockedLayer(Transaction tr, Entity entity)
        {
            if (entity == null || entity.LayerId.IsNull || entity.LayerId.IsErased)
            {
                return false;
            }

            LayerTableRecord layer = tr.GetObject(entity.LayerId, OpenMode.ForRead, false) as LayerTableRecord;
            return layer != null && layer.IsLocked;
        }

        private static void AddExistingLayerName(LayerTable layerTable, HashSet<string> layerNames, string layerName)
        {
            if (!string.IsNullOrEmpty(layerName) && layerTable.Has(layerName))
            {
                layerNames.Add(layerName);
            }
        }

        private static bool LooksLikeFillLayer(string layerName)
        {
            if (string.IsNullOrEmpty(layerName))
            {
                return false;
            }

            string upper = layerName.ToUpperInvariant();
            return upper.IndexOf("HATCH", StringComparison.Ordinal) >= 0 ||
                   upper.IndexOf("FILL", StringComparison.Ordinal) >= 0 ||
                   upper.IndexOf("SOLID", StringComparison.Ordinal) >= 0 ||
                   layerName.IndexOf("填充", StringComparison.Ordinal) >= 0 ||
                   layerName.IndexOf("剖面", StringComparison.Ordinal) >= 0;
        }

        private static void ApplyXrefLayerColor(Document doc)
        {
            Editor ed = doc.Editor;

            PromptIntegerOptions colorOptions = new PromptIntegerOptions("\n请输入外部参照图层目标色号 <35>: ");
            colorOptions.AllowNone = true;
            colorOptions.AllowNegative = false;
            colorOptions.AllowZero = false;
            colorOptions.DefaultValue = DefaultXrefColorIndex;
            PromptIntegerResult colorResult = ed.GetInteger(colorOptions);
            if (colorResult.Status == PromptStatus.Cancel)
            {
                WriteLog("Apply color cancelled at color prompt.");
                return;
            }

            int colorIndex = colorResult.Status == PromptStatus.None ? DefaultXrefColorIndex : colorResult.Value;
            if (colorIndex < 1 || colorIndex > 255)
            {
                ed.WriteMessage("\n色号必须在 1 到 255 之间。");
                return;
            }

            if (!PromptYesNo(ed, "\n将所有外部参照图层颜色改为 " + colorIndex + "，是否继续 [是(Y)/否(N)] <Y>: ", true))
            {
                ed.WriteMessage("\n已取消外参改色。");
                return;
            }

            int checkedCount = 0;
            int changedCount = 0;
            int backupCount = 0;
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                Dictionary<string, LayerColorRecord> backup = ReadColorBackup(tr, doc.Database);
                LayerTable layerTable = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);

                foreach (ObjectId layerId in layerTable)
                {
                    LayerTableRecord layer = (LayerTableRecord)tr.GetObject(layerId, OpenMode.ForRead);
                    if (!IsXrefDependentLayer(layer))
                    {
                        continue;
                    }

                    checkedCount++;
                    if (!backup.ContainsKey(layer.Name))
                    {
                        backup[layer.Name] = LayerColorRecord.FromLayer(layer);
                    }

                    if (layer.Color.ColorMethod != ColorMethod.ByAci || layer.Color.ColorIndex != colorIndex)
                    {
                        layer.UpgradeOpen();
                        layer.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)colorIndex);
                        changedCount++;
                    }
                }

                backupCount = backup.Count;
                WriteColorBackup(tr, doc.Database, backup);
                tr.Commit();
            }

            ed.Regen();
            ed.WriteMessage("\n完成：共检查 {0} 个外参图层，修改 {1} 个，已记录原颜色 {2} 个，可用 XREFPICK 选择恢复颜色。", checkedCount, changedCount, backupCount);
            WriteLog("Apply xref color. colorIndex=" + colorIndex + ", checked=" + checkedCount + ", changed=" + changedCount + ", backup=" + backupCount);
        }

        private static void RestoreXrefLayerColors(Document doc)
        {
            Editor ed = doc.Editor;
            XrefColorBackupStatus status = GetColorBackupStatus(doc.Database);
            if (!status.HasBackup)
            {
                ed.WriteMessage("\n当前图纸没有外参颜色恢复记录。");
                return;
            }

            if (!PromptYesNo(ed, "\n检测到 " + status.RecordCount + " 个外参图层颜色记录，是否恢复 [是(Y)/否(N)] <Y>: ", true))
            {
                ed.WriteMessage("\n已取消恢复。");
                return;
            }

            int restored = 0;
            int missing = 0;
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                Dictionary<string, LayerColorRecord> backup = ReadColorBackup(tr, doc.Database);
                LayerTable layerTable = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);

                foreach (KeyValuePair<string, LayerColorRecord> pair in backup)
                {
                    if (!layerTable.Has(pair.Key))
                    {
                        missing++;
                        continue;
                    }

                    LayerTableRecord layer = (LayerTableRecord)tr.GetObject(layerTable[pair.Key], OpenMode.ForWrite);
                    layer.Color = pair.Value.ToColor();
                    restored++;
                }

                RemoveColorBackup(tr, doc.Database);
                tr.Commit();
            }

            ed.Regen();
            ed.WriteMessage("\n完成：已恢复 {0} 个外参图层颜色，跳过 {1} 个不存在图层。", restored, missing);
            WriteLog("Restore xref colors. restored=" + restored + ", missing=" + missing);
        }

        private static void WriteStatus(Editor ed, XrefColorBackupStatus backupStatus)
        {
            ed.WriteMessage("\n当前状态：");
            ed.WriteMessage("\n选择过滤：{0}", enabled ? "已开启" : "已关闭");
            ed.WriteMessage("\n外参改色：{0}", backupStatus.HasBackup ? "已应用，记录 " + backupStatus.RecordCount + " 个图层" : "未改色或无恢复记录");
            ed.WriteMessage("\n局部填充隐藏：{0}", GetScopedFillBackupStatus(ed.Document.Database));
            ed.WriteMessage("\n日志路径：" + LogPath);
            ed.WriteMessage("\n配置路径：" + ConfigPath);
        }

        private static void OnDocumentCreated(object sender, DocumentCollectionEventArgs e)
        {
            HookDocument(e.Document);
        }

        private static void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
        {
            UnhookDocument(e.Document);
        }

        private static void HookDocumentEvents()
        {
            DocumentCollection docs = Application.DocumentManager;
            if (docs == null)
            {
                return;
            }

            foreach (Document doc in docs)
            {
                HookDocument(doc);
            }
        }

        private static void HookDocument(Document doc)
        {
            if (doc == null || HookedDocs.Contains(doc))
            {
                return;
            }

            doc.Editor.SelectionAdded += OnSelectionAdded;
            doc.Editor.PointMonitor += OnPointMonitor;
            doc.CommandWillStart += OnCommandWillStart;
            HookedDocs.Add(doc);
        }

        private static void UnhookDocument(Document doc)
        {
            if (doc == null || !HookedDocs.Contains(doc))
            {
                return;
            }

            doc.Editor.SelectionAdded -= OnSelectionAdded;
            doc.Editor.PointMonitor -= OnPointMonitor;
            doc.CommandWillStart -= OnCommandWillStart;
            HookedDocs.Remove(doc);
        }

        private static void OnSelectionAdded(object sender, SelectionAddedEventArgs e)
        {
            if (!enabled)
            {
                return;
            }

            Editor ed = sender as Editor;
            Document doc = GetDocumentByEditor(ed);
            if (doc == null)
            {
                return;
            }

            int removed = 0;
            Database db = doc.Database;
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                for (int i = e.AddedObjects.Count - 1; i >= 0; i--)
                {
                    SelectedObject selected = e.AddedObjects[i];
                    if (selected != null && IsIgnoredSelection(tr, selected.ObjectId))
                    {
                        e.Remove(i);
                        removed++;
                    }
                }

                tr.Commit();
            }

            if (removed > 0)
            {
                ed.UpdateScreen();
                WriteLog("SelectionAdded removed ignored objects: " + removed);
            }
        }

        private static void OnPointMonitor(object sender, PointMonitorEventArgs e)
        {
            if (!enabled || DateTime.Now.Subtract(lastPointMonitor).TotalMilliseconds < 120)
            {
                return;
            }

            lastPointMonitor = DateTime.Now;
            Editor ed = sender as Editor;
            Document doc = GetDocumentByEditor(ed);
            if (doc == null)
            {
                return;
            }

            if (!PointMonitorHitsXref(doc, e))
            {
                return;
            }

            int removed = RemoveXrefsFromCurrentSelection(doc, true);
            if (removed > 0)
            {
                WriteLog("PointMonitor cleared xref selection. removed=" + removed);
            }
        }

        private static void OnCommandWillStart(object sender, CommandEventArgs e)
        {
            if (!enabled)
            {
                return;
            }

            Document doc = sender as Document;
            if (doc == null)
            {
                return;
            }

            int removed = RemoveXrefsFromCurrentSelection(doc, true);
            if (removed > 0)
            {
                WriteLog("CommandWillStart " + e.GlobalCommandName + " removed xrefs: " + removed);
            }
        }

        private static void ApplyWindowSelectionAssist(Editor ed)
        {
            try
            {
                if (!Directory.Exists(ConfigDir))
                {
                    Directory.CreateDirectory(ConfigDir);
                }

                int pickauto = Convert.ToInt32(Application.GetSystemVariable("PICKAUTO"));
                if (!File.Exists(PickautoBackupPath))
                {
                    File.WriteAllText(PickautoBackupPath, pickauto.ToString());
                }

                int enhancedPickauto = pickauto | 2;
                if (enhancedPickauto != pickauto)
                {
                    Application.SetSystemVariable("PICKAUTO", enhancedPickauto);
                    if (ed != null)
                    {
                        ed.WriteMessage("\n已开启框选增强：PICKAUTO {0} -> {1}。", pickauto, enhancedPickauto);
                    }
                    WriteLog("PICKAUTO enhanced. previous=" + pickauto + ", current=" + enhancedPickauto);
                }
                else
                {
                    WriteLog("PICKAUTO already supports object-over-window selection. current=" + pickauto);
                }

                int pickdrag = Convert.ToInt32(Application.GetSystemVariable("PICKDRAG"));
                if (!File.Exists(PickdragBackupPath))
                {
                    File.WriteAllText(PickdragBackupPath, pickdrag.ToString());
                }

                if (pickdrag != 2)
                {
                    Application.SetSystemVariable("PICKDRAG", 2);
                    if (ed != null)
                    {
                        ed.WriteMessage("\n已开启拖框增强：PICKDRAG {0} -> 2。", pickdrag);
                    }
                    WriteLog("PICKDRAG enhanced. previous=" + pickdrag + ", current=2");
                }
                else
                {
                    WriteLog("PICKDRAG already supports both window methods. current=" + pickdrag);
                }

                int pickstyle = Convert.ToInt32(Application.GetSystemVariable("PICKSTYLE"));
                if (!File.Exists(PickstyleBackupPath))
                {
                    File.WriteAllText(PickstyleBackupPath, pickstyle.ToString());
                }

                int enhancedPickstyle = pickstyle & ~2;
                if (enhancedPickstyle != pickstyle)
                {
                    Application.SetSystemVariable("PICKSTYLE", enhancedPickstyle);
                    if (ed != null)
                    {
                        ed.WriteMessage("\n已关闭填充关联抢选：PICKSTYLE {0} -> {1}。", pickstyle, enhancedPickstyle);
                    }
                    WriteLog("PICKSTYLE hatch association disabled. previous=" + pickstyle + ", current=" + enhancedPickstyle);
                }
                else
                {
                    WriteLog("PICKSTYLE hatch association already disabled. current=" + pickstyle);
                }
            }
            catch (System.Exception ex)
            {
                WriteLog("ApplyWindowSelectionAssist failed: " + ex.Message);
            }
        }

        private static void RestoreWindowSelectionAssist(Editor ed)
        {
            try
            {
                int current = Convert.ToInt32(Application.GetSystemVariable("PICKAUTO"));
                int previous;
                if (TryRestoreSystemVariable("PICKAUTO", PickautoBackupPath, out previous))
                {
                    if (ed != null)
                    {
                        ed.WriteMessage("\n已恢复框选设置：PICKAUTO {0} -> {1}。", current, previous);
                    }
                    WriteLog("PICKAUTO restored. previous=" + previous + ", currentBeforeRestore=" + current);
                }

                current = Convert.ToInt32(Application.GetSystemVariable("PICKDRAG"));
                if (TryRestoreSystemVariable("PICKDRAG", PickdragBackupPath, out previous))
                {
                    if (ed != null)
                    {
                        ed.WriteMessage("\n已恢复拖框设置：PICKDRAG {0} -> {1}。", current, previous);
                    }
                    WriteLog("PICKDRAG restored. previous=" + previous + ", currentBeforeRestore=" + current);
                }

                current = Convert.ToInt32(Application.GetSystemVariable("PICKSTYLE"));
                if (TryRestoreSystemVariable("PICKSTYLE", PickstyleBackupPath, out previous))
                {
                    if (ed != null)
                    {
                        ed.WriteMessage("\n已恢复填充选择设置：PICKSTYLE {0} -> {1}。", current, previous);
                    }
                    WriteLog("PICKSTYLE restored. previous=" + previous + ", currentBeforeRestore=" + current);
                }
            }
            catch (System.Exception ex)
            {
                WriteLog("RestoreWindowSelectionAssist failed: " + ex.Message);
            }
        }

        private static bool TryRestoreSystemVariable(string name, string backupPath, out int previous)
        {
            previous = 0;
            if (!File.Exists(backupPath))
            {
                return false;
            }

            string text = File.ReadAllText(backupPath).Trim();
            if (!int.TryParse(text, out previous))
            {
                return false;
            }

            Application.SetSystemVariable(name, previous);
            File.Delete(backupPath);
            return true;
        }

        private static int RemoveXrefsFromCurrentSelection(Document doc, bool refresh)
        {
            PromptSelectionResult result = doc.Editor.SelectImplied();
            if (result.Status != PromptStatus.OK || result.Value == null)
            {
                return 0;
            }

            List<ObjectId> kept = new List<ObjectId>();
            int removed = 0;
            using (Transaction tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (SelectedObject selected in result.Value)
                {
                    if (selected == null)
                    {
                        continue;
                    }

                    if (IsIgnoredSelection(tr, selected.ObjectId))
                    {
                        removed++;
                    }
                    else
                    {
                        kept.Add(selected.ObjectId);
                    }
                }

                tr.Commit();
            }

            if (removed > 0)
            {
                doc.Editor.SetImpliedSelection(kept.ToArray());
                if (refresh)
                {
                    doc.Editor.UpdateScreen();
                }
            }

            return removed;
        }

        private static bool PointMonitorHitsXref(Document doc, PointMonitorEventArgs e)
        {
            try
            {
                object context = e.Context;
                if (context == null)
                {
                    return false;
                }

                MethodInfo method = context.GetType().GetMethod("GetPickedEntities", Type.EmptyTypes);
                if (method == null)
                {
                    return false;
                }

                object picked = method.Invoke(context, null);
                Array pickedArray = picked as Array;
                if (pickedArray == null || pickedArray.Length == 0)
                {
                    return false;
                }

                using (Transaction tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
                {
                    foreach (object path in pickedArray)
                    {
                        ObjectId[] ids = GetObjectIdsFromPath(path);
                        if (ids == null)
                        {
                            continue;
                        }

                        foreach (ObjectId id in ids)
                        {
                            if (IsIgnoredSelection(tr, id))
                            {
                                tr.Commit();
                                return true;
                            }
                        }
                    }

                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                WriteLog("PointMonitor check failed: " + ex.Message);
            }

            return false;
        }

        private static ObjectId[] GetObjectIdsFromPath(object path)
        {
            if (path == null)
            {
                return null;
            }

            MethodInfo method = path.GetType().GetMethod("GetObjectIds", Type.EmptyTypes);
            if (method == null)
            {
                return null;
            }

            return method.Invoke(path, null) as ObjectId[];
        }

        private static bool IsIgnoredSelection(Transaction tr, ObjectId objectId)
        {
            if (IsLockedLayerSelection(tr, objectId))
            {
                return true;
            }

            return IsExternalReferenceSelection(tr, objectId);
        }

        private static bool IsLockedLayerSelection(Transaction tr, ObjectId objectId)
        {
            if (objectId.IsNull || objectId.IsErased)
            {
                return false;
            }

            Entity entity = null;
            try
            {
                entity = tr.GetObject(objectId, OpenMode.ForRead, false) as Entity;
            }
            catch
            {
                return false;
            }

            if (entity == null || entity.LayerId.IsNull || entity.LayerId.IsErased)
            {
                return false;
            }

            LayerTableRecord layer = null;
            try
            {
                layer = tr.GetObject(entity.LayerId, OpenMode.ForRead, false) as LayerTableRecord;
            }
            catch
            {
                return false;
            }

            if (layer != null && layer.IsLocked)
            {
                WriteLog("Locked layer hit: objectId=" + objectId + ", layer=" + layer.Name + ", type=" + entity.GetType().Name);
                return true;
            }

            return false;
        }

        private static bool IsExternalReferenceSelection(Transaction tr, ObjectId objectId)
        {
            if (objectId.IsNull || objectId.IsErased)
            {
                return false;
            }

            DBObject obj = null;
            try
            {
                obj = tr.GetObject(objectId, OpenMode.ForRead, false);
            }
            catch
            {
                WriteLog("Cannot open selected object: " + objectId);
                return false;
            }

            BlockReference blockRef = obj as BlockReference;
            if (blockRef != null && IsXrefBlockTableRecord(tr, blockRef.BlockTableRecord))
            {
                WriteLog("Xref block hit: objectId=" + objectId);
                return true;
            }

            Entity entity = obj as Entity;
            if (entity != null && IsOwnedByXrefBlock(tr, entity.OwnerId))
            {
                WriteLog("Xref owned entity hit: objectId=" + objectId + ", type=" + entity.GetType().Name);
                return true;
            }

            return false;
        }

        private static bool IsXrefBlockTableRecord(Transaction tr, ObjectId blockTableRecordId)
        {
            if (blockTableRecordId.IsNull || blockTableRecordId.IsErased)
            {
                return false;
            }

            BlockTableRecord btr = null;
            try
            {
                btr = tr.GetObject(blockTableRecordId, OpenMode.ForRead, false) as BlockTableRecord;
            }
            catch
            {
                return false;
            }

            return btr != null && (btr.IsFromExternalReference || btr.IsFromOverlayReference);
        }

        private static bool IsOwnedByXrefBlock(Transaction tr, ObjectId ownerId)
        {
            int guard = 0;
            ObjectId current = ownerId;
            while (!current.IsNull && !current.IsErased && guard < 20)
            {
                guard++;
                BlockTableRecord btr = null;
                try
                {
                    btr = tr.GetObject(current, OpenMode.ForRead, false) as BlockTableRecord;
                }
                catch
                {
                    return false;
                }

                if (btr == null)
                {
                    return false;
                }

                if (btr.IsFromExternalReference || btr.IsFromOverlayReference)
                {
                    return true;
                }

                return false;
            }

            return false;
        }

        private static bool IsXrefDependentLayer(LayerTableRecord layer)
        {
            if (layer == null)
            {
                return false;
            }

            return layer.IsDependent || layer.Name.IndexOf("|", StringComparison.Ordinal) >= 0;
        }

        private static Dictionary<string, LayerStateRecord> ReadLayerStateBackup(Transaction tr, Database db, string dictionaryName)
        {
            Dictionary<string, LayerStateRecord> records = new Dictionary<string, LayerStateRecord>(StringComparer.OrdinalIgnoreCase);
            Xrecord record = GetXrecord(tr, db, dictionaryName, OpenMode.ForRead);
            if (record == null || record.Data == null)
            {
                return records;
            }

            TypedValue[] values = record.Data.AsArray();
            for (int i = 0; i + 1 < values.Length; i += 2)
            {
                string layerName = values[i].Value as string;
                if (string.IsNullOrEmpty(layerName))
                {
                    continue;
                }

                records[layerName] = new LayerStateRecord
                {
                    IsOff = Convert.ToInt16(values[i + 1].Value) != 0
                };
            }

            return records;
        }

        private static void WriteLayerStateBackup(Transaction tr, Database db, string dictionaryName, Dictionary<string, LayerStateRecord> records)
        {
            Xrecord record = GetOrCreateXrecord(tr, db, dictionaryName);
            List<TypedValue> values = new List<TypedValue>();
            foreach (KeyValuePair<string, LayerStateRecord> pair in records)
            {
                values.Add(new TypedValue((int)DxfCode.Text, pair.Key));
                values.Add(new TypedValue((int)DxfCode.Int16, pair.Value.IsOff ? 1 : 0));
            }

            record.Data = new ResultBuffer(values.ToArray());
        }

        private static int RestoreLayerStates(Transaction tr, Database db, string dictionaryName)
        {
            Dictionary<string, LayerStateRecord> backup = ReadLayerStateBackup(tr, db, dictionaryName);
            LayerTable layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            int restored = 0;

            foreach (KeyValuePair<string, LayerStateRecord> pair in backup)
            {
                if (!layerTable.Has(pair.Key))
                {
                    continue;
                }

                LayerTableRecord layer = (LayerTableRecord)tr.GetObject(layerTable[pair.Key], OpenMode.ForWrite);
                layer.IsOff = pair.Value.IsOff;
                restored++;
            }

            RemoveXrecord(tr, db, dictionaryName);
            return restored;
        }

        private static Dictionary<string, ObjectVisibilityRecord> ReadObjectVisibilityBackup(Transaction tr, Database db, string dictionaryName)
        {
            Dictionary<string, ObjectVisibilityRecord> records = new Dictionary<string, ObjectVisibilityRecord>(StringComparer.OrdinalIgnoreCase);
            Xrecord record = GetXrecord(tr, db, dictionaryName, OpenMode.ForRead);
            if (record == null || record.Data == null)
            {
                return records;
            }

            TypedValue[] values = record.Data.AsArray();
            for (int i = 0; i + 1 < values.Length; i += 2)
            {
                string handle = values[i].Value as string;
                if (string.IsNullOrEmpty(handle))
                {
                    continue;
                }

                records[handle] = new ObjectVisibilityRecord
                {
                    Visible = Convert.ToInt16(values[i + 1].Value) != 0
                };
            }

            return records;
        }

        private static void WriteObjectVisibilityBackup(Transaction tr, Database db, string dictionaryName, Dictionary<string, ObjectVisibilityRecord> records)
        {
            Xrecord record = GetOrCreateXrecord(tr, db, dictionaryName);
            List<TypedValue> values = new List<TypedValue>();
            foreach (KeyValuePair<string, ObjectVisibilityRecord> pair in records)
            {
                values.Add(new TypedValue((int)DxfCode.Text, pair.Key));
                values.Add(new TypedValue((int)DxfCode.Int16, pair.Value.Visible ? 1 : 0));
            }

            record.Data = new ResultBuffer(values.ToArray());
        }

        private static RestoreObjectVisibilityResult RestoreObjectVisibility(Transaction tr, Database db, string dictionaryName)
        {
            Dictionary<string, ObjectVisibilityRecord> backup = ReadObjectVisibilityBackup(tr, db, dictionaryName);
            RestoreObjectVisibilityResult result = new RestoreObjectVisibilityResult();

            foreach (KeyValuePair<string, ObjectVisibilityRecord> pair in backup)
            {
                ObjectId id;
                if (!TryGetObjectIdFromHandle(db, pair.Key, out id))
                {
                    result.Missing++;
                    continue;
                }

                Entity entity = tr.GetObject(id, OpenMode.ForWrite, false) as Entity;
                if (entity == null)
                {
                    result.Missing++;
                    continue;
                }

                entity.Visible = pair.Value.Visible;
                result.Restored++;
            }

            RemoveXrecord(tr, db, dictionaryName);
            return result;
        }

        private static bool TryGetObjectIdFromHandle(Database db, string handleText, out ObjectId id)
        {
            id = ObjectId.Null;
            try
            {
                long value = long.Parse(handleText, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                Handle handle = new Handle(value);
                id = db.GetObjectId(false, handle, 0);
                return !id.IsNull && !id.IsErased;
            }
            catch
            {
                return false;
            }
        }

        private static Xrecord GetXrecord(Transaction tr, Database db, string dictionaryName, OpenMode mode)
        {
            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(dictionaryName))
            {
                return null;
            }

            return tr.GetObject(nod.GetAt(dictionaryName), mode) as Xrecord;
        }

        private static Xrecord GetOrCreateXrecord(Transaction tr, Database db, string dictionaryName)
        {
            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(dictionaryName))
            {
                nod.UpgradeOpen();
                Xrecord newRecord = new Xrecord();
                nod.SetAt(dictionaryName, newRecord);
                tr.AddNewlyCreatedDBObject(newRecord, true);
            }

            return (Xrecord)tr.GetObject(nod.GetAt(dictionaryName), OpenMode.ForWrite);
        }

        private static void RemoveXrecord(Transaction tr, Database db, string dictionaryName)
        {
            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(dictionaryName))
            {
                return;
            }

            nod.UpgradeOpen();
            ObjectId recordId = nod.GetAt(dictionaryName);
            nod.Remove(dictionaryName);
            DBObject record = tr.GetObject(recordId, OpenMode.ForWrite, false);
            if (record != null)
            {
                record.Erase();
            }
        }

        private static Dictionary<string, LayerColorRecord> ReadColorBackup(Transaction tr, Database db)
        {
            Dictionary<string, LayerColorRecord> records = new Dictionary<string, LayerColorRecord>(StringComparer.OrdinalIgnoreCase);
            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(BackupDictionaryName))
            {
                return records;
            }

            Xrecord record = tr.GetObject(nod.GetAt(BackupDictionaryName), OpenMode.ForRead) as Xrecord;
            if (record == null || record.Data == null)
            {
                return records;
            }

            TypedValue[] values = record.Data.AsArray();
            for (int i = 0; i + 4 < values.Length; i += 5)
            {
                string layerName = values[i].Value as string;
                if (string.IsNullOrEmpty(layerName))
                {
                    continue;
                }

                records[layerName] = new LayerColorRecord
                {
                    Method = Convert.ToInt32(values[i + 1].Value),
                    ColorIndex = Convert.ToInt16(values[i + 2].Value),
                    Red = Convert.ToByte(values[i + 3].Value),
                    Green = Convert.ToByte(values[i + 4].Value),
                    Blue = 0
                };

                if (i + 5 < values.Length && values[i + 5].TypeCode == (int)DxfCode.Int16)
                {
                    records[layerName].Blue = Convert.ToByte(values[i + 5].Value);
                    i++;
                }
            }

            return records;
        }

        private static void WriteColorBackup(Transaction tr, Database db, Dictionary<string, LayerColorRecord> records)
        {
            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(BackupDictionaryName))
            {
                nod.UpgradeOpen();
                Xrecord newRecord = new Xrecord();
                nod.SetAt(BackupDictionaryName, newRecord);
                tr.AddNewlyCreatedDBObject(newRecord, true);
            }

            Xrecord record = (Xrecord)tr.GetObject(nod.GetAt(BackupDictionaryName), OpenMode.ForWrite);
            List<TypedValue> values = new List<TypedValue>();
            foreach (KeyValuePair<string, LayerColorRecord> pair in records)
            {
                values.Add(new TypedValue((int)DxfCode.Text, pair.Key));
                values.Add(new TypedValue((int)DxfCode.Int32, pair.Value.Method));
                values.Add(new TypedValue((int)DxfCode.Int16, pair.Value.ColorIndex));
                values.Add(new TypedValue((int)DxfCode.Int16, pair.Value.Red));
                values.Add(new TypedValue((int)DxfCode.Int16, pair.Value.Green));
                values.Add(new TypedValue((int)DxfCode.Int16, pair.Value.Blue));
            }

            record.Data = new ResultBuffer(values.ToArray());
        }

        private static void RemoveColorBackup(Transaction tr, Database db)
        {
            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(BackupDictionaryName))
            {
                return;
            }

            nod.UpgradeOpen();
            ObjectId recordId = nod.GetAt(BackupDictionaryName);
            nod.Remove(BackupDictionaryName);
            DBObject record = tr.GetObject(recordId, OpenMode.ForWrite, false);
            if (record != null)
            {
                record.Erase();
            }
        }

        private static XrefColorBackupStatus GetColorBackupStatus(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                Dictionary<string, LayerColorRecord> backup = ReadColorBackup(tr, db);
                tr.Commit();
                return new XrefColorBackupStatus { HasBackup = backup.Count > 0, RecordCount = backup.Count };
            }
        }

        private static string GetScopedFillBackupStatus(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                int layerCount = ReadLayerStateBackup(tr, db, FillLayerBackupDictionaryName).Count;
                int objectCount = ReadObjectVisibilityBackup(tr, db, FillObjectBackupDictionaryName).Count;
                tr.Commit();

                if (layerCount == 0 && objectCount == 0)
                {
                    return "正常";
                }

                return "已隐藏，记录外参填充图层 " + layerCount + " 个，锁定块填充对象 " + objectCount + " 个，可用 U 恢复";
            }
        }

        private static bool PromptYesNo(Editor ed, string message, bool defaultYes)
        {
            PromptKeywordOptions options = new PromptKeywordOptions(message);
            options.AllowNone = true;
            options.Keywords.Add("Y");
            options.Keywords.Add("N");
            options.Keywords.Default = defaultYes ? "Y" : "N";

            PromptResult result = ed.GetKeywords(options);
            if (result.Status == PromptStatus.Cancel)
            {
                return false;
            }

            string answer = NormalizeKeyword(result, defaultYes ? "Y" : "N");
            return !string.Equals(answer, "N", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeKeyword(PromptResult result, string defaultValue)
        {
            if (result == null || result.Status == PromptStatus.None || string.IsNullOrWhiteSpace(result.StringResult))
            {
                return defaultValue;
            }

            return result.StringResult.Trim().ToUpperInvariant();
        }

        private static Document GetDocumentByEditor(Editor editor)
        {
            if (editor == null)
            {
                return null;
            }

            DocumentCollection docs = Application.DocumentManager;
            if (docs == null)
            {
                return null;
            }

            foreach (Document doc in docs)
            {
                if (doc.Editor == editor)
                {
                    return doc;
                }
            }

            return null;
        }

        private static bool LoadEnabled()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    return false;
                }

                string text = File.ReadAllText(ConfigPath).Trim();
                return string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(text, "on", StringComparison.OrdinalIgnoreCase);
            }
            catch (System.Exception ex)
            {
                WriteLog("LoadEnabled failed: " + ex.Message);
                return false;
            }
        }

        private static void SaveEnabled(bool value)
        {
            try
            {
                if (!Directory.Exists(ConfigDir))
                {
                    Directory.CreateDirectory(ConfigDir);
                }

                File.WriteAllText(ConfigPath, value ? "1" : "0");
            }
            catch (System.Exception ex)
            {
                WriteLog("SaveEnabled failed: " + ex.Message);
            }
        }

        private static void WriteLog(string line)
        {
            try
            {
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine);
            }
            catch
            {
            }
        }

        private class XrefColorBackupStatus
        {
            public bool HasBackup;
            public int RecordCount;
        }

        private class LayerStateRecord
        {
            public bool IsOff;

            public static LayerStateRecord FromLayer(LayerTableRecord layer)
            {
                return new LayerStateRecord
                {
                    IsOff = layer.IsOff
                };
            }
        }

        private class ObjectVisibilityRecord
        {
            public bool Visible;

            public static ObjectVisibilityRecord FromEntity(Entity entity)
            {
                return new ObjectVisibilityRecord
                {
                    Visible = entity.Visible
                };
            }
        }

        private class RestoreObjectVisibilityResult
        {
            public int Restored;
            public int Missing;
        }

        private class LayerColorRecord
        {
            public int Method;
            public short ColorIndex;
            public byte Red;
            public byte Green;
            public byte Blue;

            public static LayerColorRecord FromLayer(LayerTableRecord layer)
            {
                Color color = layer.Color;
                return new LayerColorRecord
                {
                    Method = (int)color.ColorMethod,
                    ColorIndex = color.ColorIndex,
                    Red = color.Red,
                    Green = color.Green,
                    Blue = color.Blue
                };
            }

            public Color ToColor()
            {
                ColorMethod method = (ColorMethod)Method;
                if (method == ColorMethod.ByColor)
                {
                    return Color.FromRgb(Red, Green, Blue);
                }

                return Color.FromColorIndex(method, ColorIndex);
            }
        }
    }
}
