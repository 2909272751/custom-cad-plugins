using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace beamcolor
{
    public class beamcolorCommand : IExtensionApplication
    {
        private const string ConfirmYes = "Y";
        private const string ConfirmNo = "N";
        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "BEAMCOLOR.log");

        public void Initialize()
        {
        }

        public void Terminate()
        {
        }

        [CommandMethod("BEAMCOLOR")]
        public void ColorBeamByName()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;
            List<ObjectId> previewIds = new List<ObjectId>();
            List<string> logLines = new List<string>();
            logLines.Add("BEAMCOLOR started: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            try
            {
                RuleSettings settings;
                if (!PromptForRuleSettings(db, ed, out settings, logLines))
                {
                    return;
                }

                List<ColorRule> rules = new List<ColorRule>();
                while (true)
                {
                    string prefix;
                    if (!PromptForPrefix(ed, out prefix))
                    {
                        logLines.Add("Prefix input cancelled.");
                        return;
                    }

                    ColorRule rule = new ColorRule
                    {
                        Prefix = prefix,
                        SourceLayer = settings.SourceLayer,
                        TargetLayer = settings.TargetLayer,
                        TargetColor = settings.TargetColor
                    };

                    rules.Add(rule);
                    logLines.Add("Rule " + rules.Count + ": prefix=" + prefix + ", sourceLayer=" + settings.SourceLayer + ", targetLayer=" + settings.TargetLayer + ", color=" + DescribeColor(settings.TargetColor));

                    string moreAnswer;
                    if (!PromptForMoreRules(ed, out moreAnswer) || string.Equals(moreAnswer, ConfirmNo, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }

                PromptSelectionResult rangeResult = PromptForRange(ed);
                if (rangeResult.Status != PromptStatus.OK)
                {
                    logLines.Add("Range selection cancelled: " + rangeResult.Status);
                    return;
                }

                PreviewResult preview;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    HashSet<ObjectId> selectedIds = SelectionToSet(rangeResult.Value);
                    logLines.Add("Range selection object count: " + selectedIds.Count);
                    preview = BuildPreview(tr, selectedIds, rules, logLines);
                    previewIds.AddRange(preview.AllIds);
                    HighlightEntities(tr, previewIds, true);
                    tr.Commit();
                }

                ed.WriteMessage(
                    string.Format(
                        "\n已高亮预览：识别梁编号 {0} 个，关联梁线 {1} 个，跳过非梁编号文字 {2} 个。",
                        preview.TextIds.Count,
                        preview.LineIds.Count,
                        preview.SkippedTextCount));

                string confirmAnswer;
                if (!PromptForConfirmApply(ed, out confirmAnswer) || string.Equals(confirmAnswer, ConfirmNo, StringComparison.OrdinalIgnoreCase))
                {
                    logLines.Add("Apply cancelled.");
                    return;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    ApplyColors(tr, preview, logLines);
                    tr.Commit();
                }

                ed.WriteMessage(string.Format("\n完成：已修改梁编号 {0} 个，梁线 {1} 个。", preview.TextIds.Count, preview.LineIds.Count));
            }
            catch (System.Exception ex)
            {
                logLines.Add("ERROR: " + ex.ToString());
                ed.WriteMessage(string.Format("\nBEAMCOLOR 执行失败：{0}", ex.Message));
            }
            finally
            {
                WriteLog(logLines);
                UnhighlightEntities(db, previewIds);
            }
        }

        [CommandMethod("BEAMCOLORLOG")]
        public void ShowLogPath()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            doc.Editor.WriteMessage("\nBEAMCOLOR log: " + LogPath);
        }

        private static bool PromptForRuleSettings(Database db, Editor ed, out RuleSettings settings, List<string> logLines)
        {
            settings = null;

            PromptEntityOptions sourceOptions = new PromptEntityOptions("\n请选择一个图层和颜色: ");
            sourceOptions.AllowNone = false;
            PromptEntityResult sourceResult = ed.GetEntity(sourceOptions);
            if (sourceResult.Status != PromptStatus.OK)
            {
                logLines.Add("Rule source pick cancelled: " + sourceResult.Status);
                return false;
            }

            string sourceLayer;
            Color targetColor;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity sourceEntity = tr.GetObject(sourceResult.ObjectId, OpenMode.ForRead) as Entity;
                if (sourceEntity == null)
                {
                    ed.WriteMessage("\n选择的对象不是有效实体，请重新执行 BEAMCOLOR。");
                    return false;
                }

                sourceLayer = sourceEntity.Layer;
                targetColor = ResolveEntityColor(db, tr, sourceEntity);
                tr.Commit();
            }

            PromptEntityOptions targetOptions = new PromptEntityOptions("\n请选择目标图层上的一个对象: ");
            targetOptions.AllowNone = false;
            PromptEntityResult targetResult = ed.GetEntity(targetOptions);
            if (targetResult.Status != PromptStatus.OK)
            {
                logLines.Add("Rule target layer pick cancelled: " + targetResult.Status);
                return false;
            }

            string targetLayer;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity targetEntity = tr.GetObject(targetResult.ObjectId, OpenMode.ForRead) as Entity;
                if (targetEntity == null)
                {
                    ed.WriteMessage("\n选择的对象不是有效实体，请重新执行 BEAMCOLOR。");
                    return false;
                }

                targetLayer = targetEntity.Layer;
                tr.Commit();
            }

            settings = new RuleSettings
            {
                SourceLayer = sourceLayer,
                TargetLayer = targetLayer,
                TargetColor = targetColor
            };

            logLines.Add("Rule settings: sourceLayer=" + sourceLayer + ", targetLayer=" + targetLayer + ", color=" + DescribeColor(targetColor));
            return true;
        }

        private static bool PromptForPrefix(Editor ed, out string prefix)
        {
            prefix = null;
            while (true)
            {
                PromptStringOptions options = new PromptStringOptions("\n请输入匹配编号开头规则，例如 L: ");
                options.AllowSpaces = false;
                PromptResult result = ed.GetString(options);
                if (result.Status != PromptStatus.OK)
                {
                    return false;
                }

                string value = (result.StringResult ?? string.Empty).Trim().ToUpperInvariant();
                if (Regex.IsMatch(value, @"^[A-Z]+$"))
                {
                    prefix = value;
                    return true;
                }

                ed.WriteMessage("\n规则只能输入英文字母，例如 L、KL、LLK，请重新输入。");
            }
        }

        private static bool PromptForMoreRules(Editor ed, out string answer)
        {
            return PromptForYesNo(ed, "\n是否选择更多匹配编号开头规则 [是(Y)/否(N)] <N>: ", ConfirmNo, out answer);
        }

        private static PromptSelectionResult PromptForRange(Editor ed)
        {
            PromptSelectionOptions options = new PromptSelectionOptions();
            options.MessageForAdding = "\n请框选需要处理的范围: ";
            options.AllowDuplicates = false;
            return ed.GetSelection(options);
        }

        private static bool PromptForConfirmApply(Editor ed, out string answer)
        {
            return PromptForYesNo(ed, "\n识别到的对象已高亮，是否确认修改 [是(Y)/否(N)] <Y>: ", ConfirmYes, out answer);
        }

        private static bool PromptForYesNo(Editor ed, string message, string defaultValue, out string answer)
        {
            answer = defaultValue;
            while (true)
            {
                PromptStringOptions options = new PromptStringOptions(message);
                options.AllowSpaces = false;
                PromptResult result = ed.GetString(options);
                if (result.Status == PromptStatus.None)
                {
                    answer = defaultValue;
                    return true;
                }

                if (result.Status != PromptStatus.OK)
                {
                    return false;
                }

                string value = (result.StringResult ?? string.Empty).Trim().ToUpperInvariant();
                if (value.Length == 0)
                {
                    answer = defaultValue;
                    return true;
                }

                if (value == ConfirmYes || value == "YES" || value == "是")
                {
                    answer = ConfirmYes;
                    return true;
                }

                if (value == ConfirmNo || value == "NO" || value == "否")
                {
                    answer = ConfirmNo;
                    return true;
                }

                ed.WriteMessage("\n请输入 Y 或 N，也可以直接回车使用默认值。");
            }
        }

        private static PreviewResult BuildPreview(Transaction tr, HashSet<ObjectId> selectedIds, List<ColorRule> rules, List<string> logLines)
        {
            PreviewResult preview = new PreviewResult();
            List<BeamLineInfo> beamLines = new List<BeamLineInfo>();
            List<BeamTextInfo> beamTexts = new List<BeamTextInfo>();

            foreach (ObjectId id in selectedIds)
            {
                Entity entity = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (entity == null || entity.IsErased)
                {
                    continue;
                }

                if (IsTextEntity(entity))
                {
                    string rawText = GetTextString(entity);
                    string normalized = NormalizeText(rawText);
                    string beamName = ExtractBeamNameCandidate(normalized);
                    ColorRule matchedRule = FindRuleForBeamName(beamName, rules);
                    if (matchedRule == null)
                    {
                        preview.SkippedTextCount++;
                        logLines.Add("SKIP text " + FormatObjectId(id) + ": raw=" + rawText + ", normalized=" + normalized + ", candidate=" + beamName + ", layer=" + entity.Layer);
                        continue;
                    }

                    BeamTextInfo textInfo = GetTextInfo(entity);
                    if (textInfo == null)
                    {
                        logLines.Add("SKIP no extents " + FormatObjectId(id) + ": " + rawText);
                        continue;
                    }

                    textInfo.Id = id;
                    textInfo.Rule = matchedRule;
                    textInfo.Text = beamName;
                    beamTexts.Add(textInfo);
                    logLines.Add("BEAM TEXT " + FormatObjectId(id) + ": " + beamName + ", normalized=" + normalized + ", layer=" + entity.Layer + ", rule=" + matchedRule.Prefix);
                    continue;
                }

                if (IsCurveEntity(entity))
                {
                    ColorRule lineRule = FindRuleForTargetLayer(entity.Layer, rules);
                    if (lineRule != null)
                    {
                        AddBeamLineInfos(entity, id, beamLines, lineRule);
                    }
                }
            }

            foreach (BeamTextInfo text in beamTexts)
            {
                preview.TextIds.Add(text.Id);
                preview.ColorById[text.Id] = text.Rule.TargetColor;

                BeamLineInfo nearest = FindNearestLine(text, beamLines);
                if (nearest != null)
                {
                    preview.LineIds.Add(nearest.Id);
                    preview.ColorById[nearest.Id] = text.Rule.TargetColor;
                    logLines.Add("MATCH line " + FormatObjectId(nearest.Id) + " for text " + FormatObjectId(text.Id) + ", distance=" + nearest.DistanceTo(text.Center).ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    logLines.Add("NO LINE for text " + FormatObjectId(text.Id));
                }
            }

            return preview;
        }

        private static ColorRule FindRuleForBeamName(string beamName, List<ColorRule> rules)
        {
            ColorRule best = null;
            foreach (ColorRule rule in rules)
            {
                if (IsBeamNameMatch(beamName, rule.Prefix))
                {
                    if (best == null || rule.Prefix.Length > best.Prefix.Length)
                    {
                        best = rule;
                    }
                }
            }

            return best;
        }

        private static ColorRule FindRuleForTargetLayer(string layerName, List<ColorRule> rules)
        {
            foreach (ColorRule rule in rules)
            {
                if (string.Equals(layerName, rule.TargetLayer, StringComparison.OrdinalIgnoreCase))
                {
                    return rule;
                }
            }

            return null;
        }

        private static string ExtractBeamNameCandidate(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            Match match = Regex.Match(text, @"^[A-Z]+[-_]*\d+[A-Z0-9]*(\([^)]*\))?", RegexOptions.IgnoreCase);
            return match.Success ? match.Value : string.Empty;
        }

        private static bool IsBeamNameMatch(string text, string prefix)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string pattern = "^" + Regex.Escape(prefix) + @"[-_]*\d+[A-Z0-9]*(\([^)]*\))?$";
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
        }

        private static BeamLineInfo FindNearestLine(BeamTextInfo text, List<BeamLineInfo> lines)
        {
            BeamLineInfo best = null;
            double bestDistance = double.MaxValue;
            double maxDistance = Math.Max(text.Width, text.Height) * 8.0;

            foreach (BeamLineInfo line in lines)
            {
                if (!object.ReferenceEquals(line.Rule, text.Rule))
                {
                    continue;
                }

                double distance = line.DistanceTo(text.Center);
                if (distance > maxDistance)
                {
                    continue;
                }

                if (!line.ProjectionNear(text))
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = line;
                }
            }

            return best;
        }

        private static void ApplyColors(Transaction tr, PreviewResult preview, List<string> logLines)
        {
            foreach (KeyValuePair<ObjectId, Color> item in preview.ColorById)
            {
                Entity entity = tr.GetObject(item.Key, OpenMode.ForWrite, false) as Entity;
                if (entity == null)
                {
                    continue;
                }

                entity.Color = CloneColor(item.Value);
                logLines.Add("COLOR " + FormatObjectId(item.Key) + " -> " + DescribeColor(item.Value));
            }
        }

        private static void AddBeamLineInfos(Entity entity, ObjectId id, List<BeamLineInfo> lines, ColorRule rule)
        {
            Line line = entity as Line;
            if (line != null)
            {
                lines.Add(new BeamLineInfo(id, new Point2d(line.StartPoint.X, line.StartPoint.Y), new Point2d(line.EndPoint.X, line.EndPoint.Y), rule));
                return;
            }

            Polyline polyline = entity as Polyline;
            if (polyline != null)
            {
                int count = polyline.NumberOfVertices;
                int last = polyline.Closed ? count : count - 1;
                for (int i = 0; i < last; i++)
                {
                    if (Math.Abs(polyline.GetBulgeAt(i)) > 1e-9)
                    {
                        continue;
                    }

                    lines.Add(new BeamLineInfo(id, polyline.GetPoint2dAt(i), polyline.GetPoint2dAt((i + 1) % count), rule));
                }
            }
        }

        private static BeamTextInfo GetTextInfo(Entity entity)
        {
            try
            {
                Extents3d extents = entity.GeometricExtents;
                Point2d center = new Point2d((extents.MinPoint.X + extents.MaxPoint.X) / 2.0, (extents.MinPoint.Y + extents.MaxPoint.Y) / 2.0);
                return new BeamTextInfo
                {
                    Center = center,
                    Min = new Point2d(extents.MinPoint.X, extents.MinPoint.Y),
                    Max = new Point2d(extents.MaxPoint.X, extents.MaxPoint.Y)
                };
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeText(string text)
        {
            string normalized = StripMTextFormatting(text).Trim().ToUpperInvariant();
            normalized = normalized.Replace("\r", string.Empty);
            normalized = normalized.Replace("\n", string.Empty);
            normalized = normalized.Replace("\t", string.Empty);
            normalized = normalized.Replace(" ", string.Empty);
            normalized = normalized.Replace("\uff08", "(");
            normalized = normalized.Replace("\uff09", ")");
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

        private static bool IsCurveEntity(Entity entity)
        {
            return entity is Line || entity is Polyline;
        }

        private static Color ResolveEntityColor(Database db, Transaction tr, Entity entity)
        {
            if (entity == null)
            {
                return Color.FromColorIndex(ColorMethod.ByAci, 256);
            }

            if (entity.Color != null && entity.Color.ColorMethod != ColorMethod.ByLayer && entity.Color.ColorMethod != ColorMethod.ByBlock)
            {
                return CloneColor(entity.Color);
            }

            try
            {
                LayerTableRecord layer = tr.GetObject(entity.LayerId, OpenMode.ForRead) as LayerTableRecord;
                if (layer != null)
                {
                    return CloneColor(layer.Color);
                }
            }
            catch
            {
            }

            return CloneColor(entity.Color);
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

        private static Color CloneColor(Color color)
        {
            if (color == null)
            {
                return Color.FromColorIndex(ColorMethod.ByAci, 256);
            }

            if (color.ColorMethod == ColorMethod.ByAci)
            {
                return Color.FromColorIndex(ColorMethod.ByAci, color.ColorIndex);
            }

            return Color.FromRgb(color.Red, color.Green, color.Blue);
        }

        private static string DescribeColor(Color color)
        {
            if (color == null)
            {
                return "null";
            }

            if (color.ColorMethod == ColorMethod.ByAci)
            {
                return "ACI " + color.ColorIndex;
            }

            return "RGB(" + color.Red + "," + color.Green + "," + color.Blue + ")";
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

        private class ColorRule
        {
            public string Prefix { get; set; }

            public string SourceLayer { get; set; }

            public string TargetLayer { get; set; }

            public Color TargetColor { get; set; }
        }

        private class RuleSettings
        {
            public string SourceLayer { get; set; }

            public string TargetLayer { get; set; }

            public Color TargetColor { get; set; }
        }

        private class BeamTextInfo
        {
            public ObjectId Id { get; set; }

            public string Text { get; set; }

            public ColorRule Rule { get; set; }

            public Point2d Center { get; set; }

            public Point2d Min { get; set; }

            public Point2d Max { get; set; }

            public double Width
            {
                get { return Math.Abs(Max.X - Min.X); }
            }

            public double Height
            {
                get { return Math.Abs(Max.Y - Min.Y); }
            }
        }

        private class BeamLineInfo
        {
            public BeamLineInfo(ObjectId id, Point2d start, Point2d end, ColorRule rule)
            {
                Id = id;
                Start = start;
                End = end;
                Rule = rule;
            }

            public ObjectId Id { get; private set; }

            public Point2d Start { get; private set; }

            public Point2d End { get; private set; }

            public ColorRule Rule { get; private set; }

            public double DistanceTo(Point2d point)
            {
                double dx = End.X - Start.X;
                double dy = End.Y - Start.Y;
                double lengthSq = dx * dx + dy * dy;
                if (lengthSq < 1e-9)
                {
                    return point.GetDistanceTo(Start);
                }

                double t = ((point.X - Start.X) * dx + (point.Y - Start.Y) * dy) / lengthSq;
                t = Math.Max(0.0, Math.Min(1.0, t));
                Point2d projection = new Point2d(Start.X + t * dx, Start.Y + t * dy);
                return point.GetDistanceTo(projection);
            }

            public bool ProjectionNear(BeamTextInfo text)
            {
                double dx = Math.Abs(End.X - Start.X);
                double dy = Math.Abs(End.Y - Start.Y);
                if (dx >= dy)
                {
                    double min = Math.Min(Start.X, End.X);
                    double max = Math.Max(Start.X, End.X);
                    double tolerance = Math.Max(text.Width, text.Height) * 2.0;
                    return text.Max.X >= min - tolerance && text.Min.X <= max + tolerance;
                }

                double yMin = Math.Min(Start.Y, End.Y);
                double yMax = Math.Max(Start.Y, End.Y);
                double yTolerance = Math.Max(text.Width, text.Height) * 2.0;
                return text.Max.Y >= yMin - yTolerance && text.Min.Y <= yMax + yTolerance;
            }
        }

        private class PreviewResult
        {
            public PreviewResult()
            {
                TextIds = new HashSet<ObjectId>();
                LineIds = new HashSet<ObjectId>();
                ColorById = new Dictionary<ObjectId, Color>();
            }

            public HashSet<ObjectId> TextIds { get; private set; }

            public HashSet<ObjectId> LineIds { get; private set; }

            public Dictionary<ObjectId, Color> ColorById { get; private set; }

            public int SkippedTextCount { get; set; }

            public IEnumerable<ObjectId> AllIds
            {
                get
                {
                    HashSet<ObjectId> all = new HashSet<ObjectId>();
                    foreach (ObjectId id in TextIds)
                    {
                        all.Add(id);
                    }

                    foreach (ObjectId id in LineIds)
                    {
                        all.Add(id);
                    }

                    return all;
                }
            }
        }
    }
}
