using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace NumberTextHighlightPlugin
{
    public class NumberTextHighlightCommand : IExtensionApplication
    {
        private const string ConditionGreater = "G";
        private const string ConditionEqual = "E";
        private const string ConditionLess = "L";
        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "NUMRED.log");

        public void Initialize()
        {
        }

        public void Terminate()
        {
        }

        [CommandMethod("NUMRED")]
        public void HighlightNumberText()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;
            List<ObjectId> highlightedIds = new List<ObjectId>();
            List<string> logLines = new List<string>();
            logLines.Add("NUMRED started: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            try
            {
                PromptDoubleResult thresholdResult = PromptForThreshold(ed);
                if (thresholdResult.Status != PromptStatus.OK)
                {
                    logLines.Add("Threshold input cancelled: " + thresholdResult.Status);
                    return;
                }

                double threshold = thresholdResult.Value;
                logLines.Add("Threshold: " + threshold.ToString(CultureInfo.InvariantCulture));

                string textLayer;
                List<ObjectId> textLayerIds;
                if (!PromptAndConfirmTextLayer(db, ed, out textLayer, out textLayerIds))
                {
                    logLines.Add("Text layer selection cancelled.");
                    return;
                }

                highlightedIds.AddRange(textLayerIds);
                logLines.Add("Text layer: " + textLayer + ", text objects in current space: " + textLayerIds.Count);

                PromptSelectionResult rangeResult = PromptForRange(ed);
                if (rangeResult.Status != PromptStatus.OK)
                {
                    logLines.Add("Range selection cancelled: " + rangeResult.Status);
                    return;
                }

                PromptResult conditionResult = PromptForCondition(ed);
                if (conditionResult.Status != PromptStatus.OK)
                {
                    logLines.Add("Condition input cancelled: " + conditionResult.Status);
                    return;
                }

                string condition = conditionResult.StringResult;
                logLines.Add("Condition: " + condition);

                HighlightStats stats;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    HashSet<ObjectId> selectedIds = SelectionToSet(rangeResult.Value);
                    logLines.Add("Range selection object count: " + selectedIds.Count);
                    stats = ApplyHighlight(tr, selectedIds, textLayer, threshold, condition, logLines);
                    tr.Commit();
                }

                ed.WriteMessage(
                    string.Format(
                        "\n\u5b8c\u6210\uff1a\u5171\u68c0\u67e5 {0} \u4e2a\u6587\u5b57\uff0c\u8bc6\u522b\u51fa {1} \u4e2a\u6570\u5b57\uff0c\u6807\u7ea2 {2} \u4e2a\uff0c\u8df3\u8fc7 {3} \u4e2a\u975e\u6570\u5b57\u6587\u5b57\u3002",
                        stats.CheckedTextCount,
                        stats.ParsedNumberCount,
                        stats.HighlightedCount,
                        stats.SkippedNonNumberCount));
            }
            catch (System.Exception ex)
            {
                logLines.Add("ERROR: " + ex.ToString());
                ed.WriteMessage(string.Format("\nNUMRED \u6267\u884c\u5931\u8d25\uff1a{0}", ex.Message));
            }
            finally
            {
                WriteLog(logLines);
                UnhighlightEntities(db, highlightedIds);
            }
        }

        [CommandMethod("NUMREDLOG")]
        public void ShowLogPath()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            doc.Editor.WriteMessage("\nNUMRED log: " + LogPath);
        }

        private static PromptDoubleResult PromptForThreshold(Editor ed)
        {
            PromptDoubleOptions options = new PromptDoubleOptions("\n\u8bf7\u8f93\u5165\u7528\u4e8e\u6bd4\u8f83\u7684\u6570\u503c: ");
            options.AllowNone = false;
            return ed.GetDouble(options);
        }

        private static bool PromptAndConfirmTextLayer(Database db, Editor ed, out string textLayer, out List<ObjectId> textLayerIds)
        {
            textLayer = null;
            textLayerIds = new List<ObjectId>();

            PromptEntityOptions pickOptions = new PromptEntityOptions("\n\u8bf7\u9009\u62e9\u4e00\u4e2a\u6587\u5b57\u5bf9\u8c61\uff0c\u7528\u4e8e\u786e\u5b9a\u6587\u5b57\u56fe\u5c42: ");
            pickOptions.AllowNone = false;
            PromptEntityResult pickResult = ed.GetEntity(pickOptions);
            if (pickResult.Status != PromptStatus.OK)
            {
                return false;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity source = tr.GetObject(pickResult.ObjectId, OpenMode.ForRead) as Entity;
                if (!IsTextEntity(source))
                {
                    ed.WriteMessage("\n\u9009\u62e9\u7684\u4e0d\u662f\u6587\u5b57\u5bf9\u8c61\uff0c\u8bf7\u91cd\u65b0\u6267\u884c NUMRED\u3002");
                    return false;
                }

                textLayer = source.Layer;
                textLayerIds = CollectTextEntityIds(db, tr, textLayer);
                HighlightEntities(tr, textLayerIds, true);
                tr.Commit();
            }

            ed.WriteMessage(string.Format("\n\u5df2\u9009\u4e2d\u6587\u5b57\u56fe\u5c42\uff1a{0}\uff0c\u5f53\u524d\u7a7a\u95f4\u5171\u627e\u5230 {1} \u4e2a\u6587\u5b57\u5bf9\u8c61\u3002", textLayer, textLayerIds.Count));

            PromptKeywordOptions confirmOptions = new PromptKeywordOptions("\n\u662f\u5426\u786e\u8ba4\u4f7f\u7528\u8be5\u6587\u5b57\u56fe\u5c42 [\u662f(Y)/\u5426(N)] <Y>: ");
            confirmOptions.Keywords.Add("Y");
            confirmOptions.Keywords.Add("N");
            confirmOptions.Keywords.Default = "Y";
            confirmOptions.AllowNone = true;
            PromptResult confirmResult = ed.GetKeywords(confirmOptions);

            if (confirmResult.Status != PromptStatus.OK || string.Equals(confirmResult.StringResult, "N", StringComparison.OrdinalIgnoreCase))
            {
                UnhighlightEntities(db, textLayerIds);
                ed.WriteMessage("\n\u5df2\u53d6\u6d88\uff0c\u8bf7\u91cd\u65b0\u6267\u884c NUMRED\u3002");
                return false;
            }

            return true;
        }

        private static PromptSelectionResult PromptForRange(Editor ed)
        {
            PromptSelectionOptions options = new PromptSelectionOptions();
            options.MessageForAdding = "\n\u8bf7\u6846\u9009\u9700\u8981\u68c0\u67e5\u7684\u6587\u5b57\u8303\u56f4: ";
            options.AllowDuplicates = false;
            return ed.GetSelection(options);
        }

        private static PromptResult PromptForCondition(Editor ed)
        {
            PromptKeywordOptions options = new PromptKeywordOptions("\n\u8bf7\u9009\u62e9\u5224\u65ad\u6761\u4ef6 [\u5927\u4e8e(G)/\u7b49\u4e8e(E)/\u5c0f\u4e8e(L)] <G>: ");
            options.Keywords.Add(ConditionGreater);
            options.Keywords.Add(ConditionEqual);
            options.Keywords.Add(ConditionLess);
            options.Keywords.Default = ConditionGreater;
            options.AllowNone = true;
            return ed.GetKeywords(options);
        }

        private static HighlightStats ApplyHighlight(
            Transaction tr,
            IEnumerable<ObjectId> selectedIds,
            string textLayer,
            double threshold,
            string condition,
            List<string> logLines)
        {
            HighlightStats stats = new HighlightStats();

            foreach (ObjectId id in selectedIds)
            {
                Entity entity = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (!IsTextEntity(entity) || !string.Equals(entity.Layer, textLayer, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                stats.CheckedTextCount++;
                string rawText = GetTextString(entity);
                double value;
                if (!TryParseStrictNumber(rawText, out value))
                {
                    stats.SkippedNonNumberCount++;
                    logLines.Add("SKIP non-number " + FormatObjectId(id) + ": " + rawText);
                    continue;
                }

                stats.ParsedNumberCount++;
                bool matched = IsMatch(value, threshold, condition);
                logLines.Add("TEXT " + FormatObjectId(id) + ": value=" + value.ToString(CultureInfo.InvariantCulture) + ", matched=" + matched);
                if (!matched)
                {
                    continue;
                }

                Entity writable = (Entity)tr.GetObject(id, OpenMode.ForWrite, false);
                writable.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
                stats.HighlightedCount++;
            }

            return stats;
        }

        private static bool IsMatch(double value, double threshold, string condition)
        {
            if (string.Equals(condition, ConditionEqual, StringComparison.OrdinalIgnoreCase))
            {
                double tolerance = Math.Max(1e-9, Math.Max(Math.Abs(value), Math.Abs(threshold)) * 1e-9);
                return Math.Abs(value - threshold) <= tolerance;
            }

            if (string.Equals(condition, ConditionLess, StringComparison.OrdinalIgnoreCase))
            {
                return value < threshold;
            }

            return value > threshold;
        }

        private static bool TryParseStrictNumber(string text, out double value)
        {
            value = 0.0;
            if (text == null)
            {
                return false;
            }

            string normalized = NormalizeNumberText(text);
            if (normalized.Length == 0)
            {
                return false;
            }

            if (!Regex.IsMatch(normalized, @"^[+-]?(\d+(\.\d*)?|\.\d+)$"))
            {
                return false;
            }

            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string NormalizeNumberText(string text)
        {
            string normalized = StripMTextFormatting(text).Trim();
            normalized = normalized.Replace("\u2212", "-");
            normalized = normalized.Replace("\uff0b", "+");
            normalized = normalized.Replace("\uff0d", "-");
            normalized = normalized.Replace("\uff0e", ".");
            normalized = normalized.Replace("\uff10", "0");
            normalized = normalized.Replace("\uff11", "1");
            normalized = normalized.Replace("\uff12", "2");
            normalized = normalized.Replace("\uff13", "3");
            normalized = normalized.Replace("\uff14", "4");
            normalized = normalized.Replace("\uff15", "5");
            normalized = normalized.Replace("\uff16", "6");
            normalized = normalized.Replace("\uff17", "7");
            normalized = normalized.Replace("\uff18", "8");
            normalized = normalized.Replace("\uff19", "9");
            return normalized;
        }

        private static string StripMTextFormatting(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string result = text.Replace("\\P", " ");
            result = Regex.Replace(result, @"\\[A-Za-z][^;]*;", string.Empty);
            result = result.Replace("{", string.Empty).Replace("}", string.Empty);
            return result;
        }

        private static string GetTextString(Entity entity)
        {
            DBText dbText = entity as DBText;
            if (dbText != null)
            {
                return dbText.TextString;
            }

            MText mText = entity as MText;
            if (mText != null)
            {
                return mText.Contents;
            }

            return string.Empty;
        }

        private static bool IsTextEntity(Entity entity)
        {
            return entity is DBText || entity is MText;
        }

        private static List<ObjectId> CollectTextEntityIds(Database db, Transaction tr, string layerName)
        {
            List<ObjectId> ids = new List<ObjectId>();
            BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

            foreach (ObjectId id in currentSpace)
            {
                Entity entity = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (IsTextEntity(entity) && string.Equals(entity.Layer, layerName, StringComparison.OrdinalIgnoreCase))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        private static HashSet<ObjectId> SelectionToSet(SelectionSet selectionSet)
        {
            HashSet<ObjectId> ids = new HashSet<ObjectId>();

            foreach (SelectedObject selected in selectionSet)
            {
                if (selected != null && !selected.ObjectId.IsNull)
                {
                    ids.Add(selected.ObjectId);
                }
            }

            return ids;
        }

        private static void HighlightEntities(Transaction tr, IEnumerable<ObjectId> ids, bool highlight)
        {
            foreach (ObjectId id in ids)
            {
                Entity entity = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (entity == null)
                {
                    continue;
                }

                if (highlight)
                {
                    entity.Highlight();
                }
                else
                {
                    entity.Unhighlight();
                }
            }
        }

        private static void UnhighlightEntities(Database db, IEnumerable<ObjectId> ids)
        {
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    HighlightEntities(tr, ids, false);
                    tr.Commit();
                }
            }
            catch
            {
            }
        }

        private static string FormatObjectId(ObjectId id)
        {
            try
            {
                return id.Handle.ToString();
            }
            catch
            {
                return id.ToString();
            }
        }

        private static void WriteLog(IEnumerable<string> lines)
        {
            try
            {
                File.AppendAllLines(LogPath, lines);
                File.AppendAllText(LogPath, Environment.NewLine);
            }
            catch
            {
            }
        }

        private class HighlightStats
        {
            public int CheckedTextCount { get; set; }

            public int ParsedNumberCount { get; set; }

            public int HighlightedCount { get; set; }

            public int SkippedNonNumberCount { get; set; }
        }
    }
}
