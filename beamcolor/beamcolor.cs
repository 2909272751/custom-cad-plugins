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
                List<ColorRule> rules = new List<ColorRule>();
                while (true)
                {
                    ColorRule rule;
                    if (!PromptForRule(db, ed, rules.Count + 1, out rule, logLines))
                    {
                        return;
                    }

                    rules.Add(rule);
                    PromptResult moreResult = PromptForMoreRules(ed);
                    if (moreResult.Status != PromptStatus.OK || string.Equals(moreResult.StringResult, ConfirmNo, StringComparison.OrdinalIgnoreCase))
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

                PromptResult confirmResult = PromptForConfirmApply(ed);
                if (confirmResult.Status != PromptStatus.OK || string.Equals(confirmResult.StringResult, ConfirmNo, StringComparison.OrdinalIgnoreCase))
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

        private static bool PromptForRule(Database db, Editor ed, int ruleIndex, out ColorRule rule, List<string> logLines)
        {
            rule = null;

            PromptEntityOptions textOptions = new PromptEntityOptions("\n请选择一个梁编号文字，用于确定文字图层和目标颜色: ");
            textOptions.AllowNone = false;
            PromptEntityResult textResult = ed.GetEntity(textOptions);
            if (textResult.Status != PromptStatus.OK)
            {
                logLines.Add("Rule text pick cancelled: " + textResult.Status);
                return false;
            }

            string textLayer;
            Color targetColor;
            string sampleText;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity textEntity = tr.GetObject(textResult.ObjectId, OpenMode.ForRead) as Entity;
                if (!IsTextEntity(textEntity))
                {
                    ed.WriteMessage("\n选择的对象不是文字，请重新执行 BEAMCOLOR。");
                    return false;
                }

                textLayer = textEntity.Layer;
                targetColor = textEntity.Color;
                sampleText = GetTextString(textEntity);
                tr.Commit();
            }

            PromptEntityOptions lineOptions = new PromptEntityOptions("\n请选择一条对应梁线，用于确定梁线图层: ");
            lineOptions.AllowNone = false;
            PromptEntityResult lineResult = ed.GetEntity(lineOptions);
            if (lineResult.Status != PromptStatus.OK)
            {
                logLines.Add("Rule beam line pick cancelled: " + lineResult.Status);
                return false;
            }

            string beamLayer;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity lineEntity = tr.GetObject(lineResult.ObjectId, OpenMode.ForRead) as Entity;
                if (!IsCurveEntity(lineEntity))
                {
                    ed.WriteMessage("\n选择的对象不是梁线，请重新执行 BEAMCOLOR。");
                    return false;
                }

                beamLayer = lineEntity.Layer;
                tr.Commit();
            }

            string prefix;
            if (!PromptForPrefix(ed, out prefix))
            {
                logLines.Add("Prefix input cancelled.");
                return false;
            }

            rule = new ColorRule
            {
                Prefix = prefix,
                TextLayer = textLayer,
                BeamLayer = beamLayer,
                TargetColor = targetColor
            };

            logLines.Add("Rule " + ruleIndex + ": prefix=" + prefix + ", sample=" + sampleText + ", textLayer=" + textLayer + ", beamLayer=" + beamLayer + ", color=" + DescribeColor(targetColor));
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

        private static PromptResult PromptForMoreRules(Editor ed)
        {
            PromptKeywordOptions options = new PromptKeywordOptions("\n是否选择更多匹配编号开头规则 [是(Y)/否(N)] <N>: ");
            options.Keywords.Add(ConfirmYes);
            options.Keywords.Add(ConfirmNo);
            options.Keywords.Default = ConfirmNo;
            options.AllowNone = true;
            return ed.GetKeywords(options);
        }

        private static PromptSelectionResult PromptForRange(Editor ed)
        {
            PromptSelectionOptions options = new PromptSelectionOptions();
            options.MessageForAdding = "\n请框选需要处理的范围: ";
            options.AllowDuplicates = false;
            return ed.GetSelection(options);
        }

        private static PromptResult PromptForConfirmApply(Editor ed)
        {
            PromptKeywordOptions options = new PromptKeywordOptions("\n识别到的对象已高亮，是否确认修改 [是(Y)/否(N)] <Y>: ");
            options.Keywords.Add(ConfirmYes);
            options.Keywords.Add(ConfirmNo);
            options.Keywords.Default = ConfirmYes;
            options.AllowNone = true;
            return ed.GetKeywords(options);
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

                foreach (ColorRule rule in rules)
                {
                    if (IsTextEntity(entity) && string.Equals(entity.Layer, rule.TextLayer, StringComparison.OrdinalIgnoreCase))
                    {
                        string rawText = GetTextString(entity);
                        string normalized = NormalizeText(rawText);
                        ColorRule matchedRule = FindRuleForBeamName(normalized, rules);
                        if (matchedRule == null)
                        {
                            preview.SkippedTextCount++;
                            logLines.Add("SKIP text " + FormatObjectId(id) + ": " + rawText);
                            break;
                        }

                        BeamTextInfo textInfo = GetTextInfo(entity);
                        if (textInfo == null)
                        {
                            break;
                        }

                        textInfo.Id = id;
                        textInfo.Rule = matchedRule;
                        textInfo.Text = normalized;
                        beamTexts.Add(textInfo);
                        logLines.Add("BEAM TEXT " + FormatObjectId(id) + ": " + normalized + ", rule=" + matchedRule.Prefix);
                        break;
                    }

                    if (IsCurveEntity(entity) && string.Equals(entity.Layer, rule.BeamLayer, StringComparison.OrdinalIgnoreCase))
                    {
                        AddBeamLineInfos(entity, id, beamLines);
                        break;
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

        private static ColorRule FindRuleForBeamName(string text, List<ColorRule> rules)
        {
            ColorRule best = null;
            foreach (ColorRule rule in rules)
            {
                if (IsBeamNameMatch(text, rule.Prefix))
                {
                    if (best == null || rule.Prefix.Length > best.Prefix.Length)
                    {
                        best = rule;
                    }
                }
            }

            return best;
        }

        private static bool IsBeamNameMatch(string text, string prefix)
        {
            string pattern = "^" + Regex.Escape(prefix) + @"\d+(\([^)]+\))?$";
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
        }

        private static BeamLineInfo FindNearestLine(BeamTextInfo text, List<BeamLineInfo> lines)
        {
            BeamLineInfo best = null;
            double bestDistance = double.MaxValue;
            double maxDistance = Math.Max(text.Width, text.Height) * 8.0;

            foreach (BeamLineInfo line in lines)
            {
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

        private static void AddBeamLineInfos(Entity entity, ObjectId id, List<BeamLineInfo> lines)
        {
            Line line = entity as Line;
            if (line != null)
            {
                lines.Add(new BeamLineInfo(id, new Point2d(line.StartPoint.X, line.StartPoint.Y), new Point2d(line.EndPoint.X, line.EndPoint.Y)));
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

                    lines.Add(new BeamLineInfo(id, polyline.GetPoint2dAt(i), polyline.GetPoint2dAt((i + 1) % count)));
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
            return StripMTextFormatting(text).Trim().ToUpperInvariant().Replace(" ", string.Empty);
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

            public string TextLayer { get; set; }

            public string BeamLayer { get; set; }

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
            public BeamLineInfo(ObjectId id, Point2d start, Point2d end)
            {
                Id = id;
                Start = start;
                End = end;
            }

            public ObjectId Id { get; private set; }

            public Point2d Start { get; private set; }

            public Point2d End { get; private set; }

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
