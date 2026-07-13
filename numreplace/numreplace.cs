using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace numreplace
{
    public class numreplaceCommand : IExtensionApplication
    {
        private const string ConditionGreater = "G";
        private const string ConditionEqual = "E";
        private const string ConditionLess = "L";
        private const string ConditionRange = "R";
        private const string ReplaceFixed = "F";
        private const string ReplaceRange = "R";
        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "NUMREPLACE.log");
        private static readonly Random Random = new Random();

        public void Initialize()
        {
        }

        public void Terminate()
        {
        }

        [CommandMethod("NUMREPLACE")]
        public void ReplaceNumberText()
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
            logLines.Add("NUMREPLACE started: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            try
            {
                PromptResult conditionResult = PromptForCondition(ed);
                if (conditionResult.Status != PromptStatus.OK)
                {
                    logLines.Add("Condition input cancelled: " + conditionResult.Status);
                    return;
                }

                string condition = conditionResult.StringResult;
                logLines.Add("Condition: " + condition);

                TargetRule targetRule;
                if (!PromptForTargetRule(ed, condition, out targetRule))
                {
                    logLines.Add("Target value input cancelled.");
                    return;
                }

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

                PromptResult preserveDecimalsResult = PromptForPreserveDecimals(ed);
                if (preserveDecimalsResult.Status != PromptStatus.OK)
                {
                    logLines.Add("Preserve decimals input cancelled: " + preserveDecimalsResult.Status);
                    return;
                }

                bool preserveDecimals = !string.Equals(preserveDecimalsResult.StringResult, "N", StringComparison.OrdinalIgnoreCase);
                logLines.Add("Preserve decimals: " + preserveDecimals);

                PromptResult replaceModeResult = PromptForReplaceMode(ed);
                if (replaceModeResult.Status != PromptStatus.OK)
                {
                    logLines.Add("Replacement mode input cancelled: " + replaceModeResult.Status);
                    return;
                }

                string replaceMode = replaceModeResult.StringResult;
                ReplacementSpec replacementSpec;
                if (!PromptForReplacementSpec(ed, replaceMode, out replacementSpec))
                {
                    logLines.Add("Replacement value input cancelled.");
                    return;
                }

                logLines.Add(DescribeReplacementSpec(replacementSpec));

                ed.WriteMessage("\n\u6b63\u5728\u5904\u7406\u6587\u5b57...");

                ReplaceStats stats;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    HashSet<ObjectId> selectedIds = SelectionToSet(rangeResult.Value);
                    logLines.Add("Range selection object count: " + selectedIds.Count);
                    stats = ApplyReplacement(tr, selectedIds, textLayer, targetRule, replacementSpec, preserveDecimals, logLines);
                    tr.Commit();
                }

                ed.WriteMessage(
                    string.Format(
                        "\n\u5b8c\u6210\uff1a\u5171\u68c0\u67e5 {0} \u4e2a\u6587\u5b57\uff0c\u8bc6\u522b\u6570\u5b57 {1} \u4e2a\uff0c\u66ff\u6362 {2} \u4e2a\uff0c\u8df3\u8fc7 {3} \u4e2a\u975e\u6570\u5b57\u6587\u5b57\u3002",
                        stats.CheckedTextCount,
                        stats.ParsedNumberCount,
                        stats.ReplacedCount,
                        stats.SkippedNonNumberCount));
            }
            catch (System.Exception ex)
            {
                logLines.Add("ERROR: " + ex.ToString());
                ed.WriteMessage(string.Format("\nNUMREPLACE \u6267\u884c\u5931\u8d25\uff1a{0}", ex.Message));
            }
            finally
            {
                WriteLog(logLines);
                UnhighlightEntities(db, highlightedIds);
            }
        }

        [CommandMethod("NUMREPLACELOG")]
        public void ShowLogPath()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            doc.Editor.WriteMessage("\nNUMREPLACE log: " + LogPath);
        }

        private static PromptResult PromptForCondition(Editor ed)
        {
            PromptKeywordOptions options = new PromptKeywordOptions("\n\u8bf7\u9009\u62e9\u76ee\u6807\u6570\u503c\u5339\u914d\u65b9\u5f0f [\u5927\u4e8e(G)/\u7b49\u4e8e(E)/\u5c0f\u4e8e(L)/\u533a\u95f4(R)] <G>: ");
            options.Keywords.Add(ConditionGreater);
            options.Keywords.Add(ConditionEqual);
            options.Keywords.Add(ConditionLess);
            options.Keywords.Add(ConditionRange);
            options.Keywords.Default = ConditionGreater;
            options.AllowNone = true;
            return ed.GetKeywords(options);
        }

        private static bool PromptForTargetRule(Editor ed, string condition, out TargetRule targetRule)
        {
            targetRule = new TargetRule();
            targetRule.Condition = condition;

            if (string.Equals(condition, ConditionRange, StringComparison.OrdinalIgnoreCase))
            {
                NumberInput maxInput;
                if (!PromptForNumber(ed, "\n\u8bf7\u8f93\u5165\u76ee\u6807\u533a\u95f4\u6700\u5927\u503c: ", out maxInput))
                {
                    return false;
                }

                NumberInput minInput;
                if (!PromptForNumber(ed, "\n\u8bf7\u8f93\u5165\u76ee\u6807\u533a\u95f4\u6700\u5c0f\u503c: ", out minInput))
                {
                    return false;
                }

                targetRule.Min = Math.Min(minInput.Value, maxInput.Value);
                targetRule.Max = Math.Max(minInput.Value, maxInput.Value);
                return true;
            }

            NumberInput targetInput;
            if (!PromptForNumber(ed, "\n\u8bf7\u8f93\u5165\u76ee\u6807\u6570\u503c: ", out targetInput))
            {
                return false;
            }

            targetRule.Value = targetInput.Value;
            return true;
        }

        private static bool PromptForNumber(Editor ed, string message, out NumberInput number)
        {
            number = null;

            while (true)
            {
                PromptStringOptions options = new PromptStringOptions(message);
                options.AllowSpaces = false;
                PromptResult result = ed.GetString(options);
                if (result.Status != PromptStatus.OK)
                {
                    return false;
                }

                double value;
                int decimals;
                string normalized;
                if (TryParseStrictNumber(result.StringResult, out value, out decimals, out normalized))
                {
                    number = new NumberInput
                    {
                        Value = value,
                        DecimalPlaces = decimals,
                        NormalizedText = normalized
                    };
                    return true;
                }

                ed.WriteMessage("\n\u8f93\u5165\u7684\u4e0d\u662f\u7eaf\u6570\u5b57\uff0c\u8bf7\u91cd\u65b0\u8f93\u5165\u3002");
            }
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
                    ed.WriteMessage("\n\u9009\u62e9\u7684\u4e0d\u662f\u6587\u5b57\u5bf9\u8c61\uff0c\u8bf7\u91cd\u65b0\u6267\u884c NUMREPLACE\u3002");
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
                ed.WriteMessage("\n\u5df2\u53d6\u6d88\uff0c\u8bf7\u91cd\u65b0\u6267\u884c NUMREPLACE\u3002");
                return false;
            }

            return true;
        }

        private static PromptSelectionResult PromptForRange(Editor ed)
        {
            PromptSelectionOptions options = new PromptSelectionOptions();
            options.MessageForAdding = "\n\u8bf7\u6846\u9009\u9700\u8981\u5904\u7406\u7684\u6587\u5b57\u8303\u56f4: ";
            options.AllowDuplicates = false;
            return ed.GetSelection(options);
        }

        private static PromptResult PromptForPreserveDecimals(Editor ed)
        {
            PromptKeywordOptions options = new PromptKeywordOptions("\n\u662f\u5426\u4fdd\u7559\u539f\u6587\u5b57\u7684\u5c0f\u6570\u4f4d\u6570 [\u662f(Y)/\u5426(N)] <Y>: ");
            options.Keywords.Add("Y");
            options.Keywords.Add("N");
            options.Keywords.Default = "Y";
            options.AllowNone = true;
            return ed.GetKeywords(options);
        }

        private static PromptResult PromptForReplaceMode(Editor ed)
        {
            PromptKeywordOptions options = new PromptKeywordOptions("\n\u8bf7\u9009\u62e9\u66ff\u6362\u65b9\u5f0f [\u56fa\u5b9a\u503c(F)/\u533a\u95f4\u968f\u673a(R)] <F>: ");
            options.Keywords.Add(ReplaceFixed);
            options.Keywords.Add(ReplaceRange);
            options.Keywords.Default = ReplaceFixed;
            options.AllowNone = true;
            return ed.GetKeywords(options);
        }

        private static bool PromptForReplacementSpec(Editor ed, string replaceMode, out ReplacementSpec replacementSpec)
        {
            replacementSpec = null;

            if (string.Equals(replaceMode, ReplaceRange, StringComparison.OrdinalIgnoreCase))
            {
                NumberInput replaceMinInput;
                if (!PromptForNumber(ed, "\n\u8bf7\u8f93\u5165\u66ff\u6362\u540e\u7684\u6700\u5c0f\u503c: ", out replaceMinInput))
                {
                    return false;
                }

                NumberInput replaceMaxInput;
                if (!PromptForNumber(ed, "\n\u8bf7\u8f93\u5165\u66ff\u6362\u540e\u7684\u6700\u5927\u503c: ", out replaceMaxInput))
                {
                    return false;
                }

                ReplacementRange range = CreateReplacementRange(replaceMinInput, replaceMaxInput);
                replacementSpec = new ReplacementSpec
                {
                    IsFixed = false,
                    FixedValue = 0.0,
                    Min = range.Min,
                    Max = range.Max,
                    DecimalPlaces = range.DecimalPlaces
                };
                return true;
            }

            NumberInput fixedInput;
            if (!PromptForNumber(ed, "\n\u8bf7\u8f93\u5165\u66ff\u6362\u540e\u7684\u56fa\u5b9a\u6570\u503c: ", out fixedInput))
            {
                return false;
            }

            replacementSpec = new ReplacementSpec
            {
                IsFixed = true,
                FixedValue = fixedInput.Value,
                Min = fixedInput.Value,
                Max = fixedInput.Value,
                DecimalPlaces = fixedInput.DecimalPlaces
            };
            return true;
        }

        private static ReplaceStats ApplyReplacement(
            Transaction tr,
            IEnumerable<ObjectId> selectedIds,
            string textLayer,
            TargetRule targetRule,
            ReplacementSpec replacementSpec,
            bool preserveDecimals,
            List<string> logLines)
        {
            ReplaceStats stats = new ReplaceStats();

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
                int originalDecimalPlaces;
                string normalized;
                if (!TryParseStrictNumber(rawText, out value, out originalDecimalPlaces, out normalized))
                {
                    stats.SkippedNonNumberCount++;
                    logLines.Add("SKIP non-number " + FormatObjectId(id) + ": " + rawText);
                    continue;
                }

                stats.ParsedNumberCount++;
                bool matched = IsMatch(value, targetRule);
                logLines.Add("TEXT " + FormatObjectId(id) + ": value=" + value.ToString(CultureInfo.InvariantCulture) + ", matched=" + matched);
                if (!matched)
                {
                    continue;
                }

                int decimalPlaces = preserveDecimals ? originalDecimalPlaces : replacementSpec.DecimalPlaces;
                string newText = CreateReplacementText(replacementSpec, decimalPlaces);

                Entity writable = (Entity)tr.GetObject(id, OpenMode.ForWrite, false);
                SetTextString(writable, newText);
                stats.ReplacedCount++;
                logLines.Add("REPLACE " + FormatObjectId(id) + ": " + normalized + " -> " + newText);
            }

            return stats;
        }

        private static bool IsMatch(double value, TargetRule targetRule)
        {
            if (string.Equals(targetRule.Condition, ConditionEqual, StringComparison.OrdinalIgnoreCase))
            {
                double tolerance = Math.Max(1e-9, Math.Max(Math.Abs(value), Math.Abs(targetRule.Value)) * 1e-9);
                return Math.Abs(value - targetRule.Value) <= tolerance;
            }

            if (string.Equals(targetRule.Condition, ConditionLess, StringComparison.OrdinalIgnoreCase))
            {
                return value < targetRule.Value;
            }

            if (string.Equals(targetRule.Condition, ConditionRange, StringComparison.OrdinalIgnoreCase))
            {
                return value >= targetRule.Min && value <= targetRule.Max;
            }

            return value > targetRule.Value;
        }

        private static ReplacementRange CreateReplacementRange(NumberInput minInput, NumberInput maxInput)
        {
            double min = Math.Min(minInput.Value, maxInput.Value);
            double max = Math.Max(minInput.Value, maxInput.Value);
            return new ReplacementRange
            {
                Min = min,
                Max = max,
                DecimalPlaces = Math.Max(minInput.DecimalPlaces, maxInput.DecimalPlaces)
            };
        }

        private static string DescribeReplacementSpec(ReplacementSpec replacementSpec)
        {
            if (replacementSpec.IsFixed)
            {
                return "Replacement mode: fixed, value=" + replacementSpec.FixedValue.ToString(CultureInfo.InvariantCulture);
            }

            return "Replacement mode: range, min=" + replacementSpec.Min.ToString(CultureInfo.InvariantCulture) + ", max=" + replacementSpec.Max.ToString(CultureInfo.InvariantCulture);
        }

        private static string CreateReplacementText(ReplacementSpec replacementSpec, int decimalPlaces)
        {
            if (replacementSpec.IsFixed)
            {
                return FormatNumber(replacementSpec.FixedValue, decimalPlaces);
            }

            if (decimalPlaces <= 0)
            {
                int min = (int)Math.Ceiling(replacementSpec.Min);
                int max = (int)Math.Floor(replacementSpec.Max);
                if (max < min)
                {
                    double value = replacementSpec.Min + Random.NextDouble() * (replacementSpec.Max - replacementSpec.Min);
                    return FormatNumber(value, 0);
                }

                return Random.Next(min, max + 1).ToString(CultureInfo.InvariantCulture);
            }

            double randomValue = replacementSpec.Min + Random.NextDouble() * (replacementSpec.Max - replacementSpec.Min);
            return FormatNumber(randomValue, decimalPlaces);
        }

        private static string FormatNumber(double value, int decimalPlaces)
        {
            if (decimalPlaces <= 0)
            {
                return Math.Round(value, 0).ToString("0", CultureInfo.InvariantCulture);
            }

            double rounded = Math.Round(value, decimalPlaces);
            string format = "0." + new string('0', decimalPlaces);
            return rounded.ToString(format, CultureInfo.InvariantCulture);
        }

        private static bool TryParseStrictNumber(string text, out double value, out int decimalPlaces, out string normalized)
        {
            value = 0.0;
            decimalPlaces = 0;
            normalized = string.Empty;
            if (text == null)
            {
                return false;
            }

            normalized = NormalizeNumberText(text);
            if (normalized.Length == 0)
            {
                return false;
            }

            if (!Regex.IsMatch(normalized, @"^[+-]?(\d+(\.\d*)?|\.\d+)$"))
            {
                return false;
            }

            int dotIndex = normalized.IndexOf('.');
            if (dotIndex >= 0)
            {
                decimalPlaces = normalized.Length - dotIndex - 1;
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

        private static void SetTextString(Entity entity, string value)
        {
            DBText dbText = entity as DBText;
            if (dbText != null)
            {
                dbText.TextString = value;
                return;
            }

            MText mText = entity as MText;
            if (mText != null)
            {
                mText.Contents = value;
            }
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

        private class NumberInput
        {
            public double Value { get; set; }

            public int DecimalPlaces { get; set; }

            public string NormalizedText { get; set; }
        }

        private class TargetRule
        {
            public string Condition { get; set; }

            public double Value { get; set; }

            public double Min { get; set; }

            public double Max { get; set; }
        }

        private class ReplacementRange
        {
            public double Min { get; set; }

            public double Max { get; set; }

            public int DecimalPlaces { get; set; }
        }

        private class ReplacementSpec
        {
            public bool IsFixed { get; set; }

            public double FixedValue { get; set; }

            public double Min { get; set; }

            public double Max { get; set; }

            public int DecimalPlaces { get; set; }
        }

        private class ReplaceStats
        {
            public int CheckedTextCount { get; set; }

            public int ParsedNumberCount { get; set; }

            public int ReplacedCount { get; set; }

            public int SkippedNonNumberCount { get; set; }
        }
    }
}
