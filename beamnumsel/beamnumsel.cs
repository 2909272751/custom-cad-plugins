using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace beamnumsel
{
    public class beamnumselCommand : IExtensionApplication
    {
        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "BEAMNUMSEL.log");

        public void Initialize()
        {
        }

        public void Terminate()
        {
        }

        [CommandMethod("BEAMNUMSEL")]
        public void SelectBeamNumbers()
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
            logLines.Add("BEAMNUMSEL started: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            try
            {
                string beamLayer;
                List<ObjectId> beamLayerIds;
                if (!PromptAndConfirmLayer(db, ed, "\n请选择一条梁线，用于确定梁线图层: ", "\n是否确认使用该梁线图层 [是(Y)/否(N)] <Y>: ", "BEAMNUMSEL", out beamLayer, out beamLayerIds))
                {
                    logLines.Add("Beam layer selection cancelled.");
                    return;
                }

                highlightedIds.AddRange(beamLayerIds);
                logLines.Add("Beam layer: " + beamLayer + ", entities in current space: " + beamLayerIds.Count);

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

                ed.WriteMessage("\n正在识别梁上数字...");

                SelectStats stats;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    HashSet<ObjectId> selectedIds = SelectionToSet(rangeResult.Value);
                    logLines.Add("Range selection object count: " + selectedIds.Count);
                    stats = SelectMatchingTexts(tr, selectedIds, beamLayer, textLayer, logLines);

                    if (stats.MatchedTextIds.Count > 0)
                    {
                        ed.SetImpliedSelection(stats.MatchedTextIds.ToArray());
                        HighlightEntities(tr, stats.MatchedTextIds, true);
                        highlightedIds.AddRange(stats.MatchedTextIds);
                    }

                    tr.Commit();
                }

                ed.WriteMessage(
                    string.Format(
                        "\n完成：共检查 {0} 个文字，识别数字 {1} 个，选中 {2} 个梁上数字。水平线上方 {3} 个，竖线左边 {4} 个。",
                        stats.CheckedTextCount,
                        stats.ParsedNumberCount,
                        stats.MatchedTextIds.Count,
                        stats.HorizontalAboveCount,
                        stats.VerticalLeftCount));
            }
            catch (System.Exception ex)
            {
                logLines.Add("ERROR: " + ex.ToString());
                ed.WriteMessage(string.Format("\nBEAMNUMSEL 执行失败：{0}", ex.Message));
            }
            finally
            {
                WriteLog(logLines);
                UnhighlightEntities(db, highlightedIds);
            }
        }

        [CommandMethod("BEAMNUMSELLOG")]
        public void ShowLogPath()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            doc.Editor.WriteMessage("\nBEAMNUMSEL log: " + LogPath);
        }

        private static bool PromptAndConfirmLayer(Database db, Editor ed, string pickMessage, string confirmMessage, string commandName, out string layerName, out List<ObjectId> layerIds)
        {
            layerName = null;
            layerIds = new List<ObjectId>();

            PromptEntityOptions pickOptions = new PromptEntityOptions(pickMessage);
            pickOptions.AllowNone = false;
            PromptEntityResult pickResult = ed.GetEntity(pickOptions);
            if (pickResult.Status != PromptStatus.OK)
            {
                return false;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity source = tr.GetObject(pickResult.ObjectId, OpenMode.ForRead) as Entity;
                if (source == null)
                {
                    ed.WriteMessage("\n选择的对象不是有效实体，请重新执行 " + commandName + "。");
                    return false;
                }

                layerName = source.Layer;
                layerIds = CollectLayerEntityIds(db, tr, layerName);
                HighlightEntities(tr, layerIds, true);
                tr.Commit();
            }

            ed.WriteMessage(string.Format("\n已选择图层：{0}，当前空间共找到 {1} 个对象。", layerName, layerIds.Count));

            PromptKeywordOptions confirmOptions = new PromptKeywordOptions(confirmMessage);
            confirmOptions.Keywords.Add("Y");
            confirmOptions.Keywords.Add("N");
            confirmOptions.Keywords.Default = "Y";
            confirmOptions.AllowNone = true;
            PromptResult confirmResult = ed.GetKeywords(confirmOptions);
            if (confirmResult.Status != PromptStatus.OK || string.Equals(confirmResult.StringResult, "N", StringComparison.OrdinalIgnoreCase))
            {
                UnhighlightEntities(db, layerIds);
                ed.WriteMessage("\n已取消，请重新执行 " + commandName + "。");
                return false;
            }

            return true;
        }

        private static bool PromptAndConfirmTextLayer(Database db, Editor ed, out string textLayer, out List<ObjectId> textLayerIds)
        {
            textLayer = null;
            textLayerIds = new List<ObjectId>();

            PromptEntityOptions pickOptions = new PromptEntityOptions("\n请选择一个数字文字，用于确定文字图层: ");
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
                    ed.WriteMessage("\n选择的对象不是文字，请重新执行 BEAMNUMSEL。");
                    return false;
                }

                textLayer = source.Layer;
                textLayerIds = CollectTextEntityIds(db, tr, textLayer);
                HighlightEntities(tr, textLayerIds, true);
                tr.Commit();
            }

            ed.WriteMessage(string.Format("\n已选择文字图层：{0}，当前空间共找到 {1} 个文字对象。", textLayer, textLayerIds.Count));

            PromptKeywordOptions confirmOptions = new PromptKeywordOptions("\n是否确认使用该文字图层 [是(Y)/否(N)] <Y>: ");
            confirmOptions.Keywords.Add("Y");
            confirmOptions.Keywords.Add("N");
            confirmOptions.Keywords.Default = "Y";
            confirmOptions.AllowNone = true;
            PromptResult confirmResult = ed.GetKeywords(confirmOptions);
            if (confirmResult.Status != PromptStatus.OK || string.Equals(confirmResult.StringResult, "N", StringComparison.OrdinalIgnoreCase))
            {
                UnhighlightEntities(db, textLayerIds);
                ed.WriteMessage("\n已取消，请重新执行 BEAMNUMSEL。");
                return false;
            }

            return true;
        }

        private static PromptSelectionResult PromptForRange(Editor ed)
        {
            PromptSelectionOptions options = new PromptSelectionOptions();
            options.MessageForAdding = "\n请框选需要识别的梁和数字范围: ";
            options.AllowDuplicates = false;
            return ed.GetSelection(options);
        }

        private static SelectStats SelectMatchingTexts(Transaction tr, IEnumerable<ObjectId> selectedIds, string beamLayer, string textLayer, List<string> logLines)
        {
            SelectStats stats = new SelectStats();
            List<BeamSegment> segments = new List<BeamSegment>();
            List<TextInfo> texts = new List<TextInfo>();

            foreach (ObjectId id in selectedIds)
            {
                Entity entity = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (entity == null || entity.IsErased)
                {
                    continue;
                }

                if (string.Equals(entity.Layer, beamLayer, StringComparison.OrdinalIgnoreCase))
                {
                    AddBeamSegments(entity, id, segments, logLines);
                    continue;
                }

                if (string.Equals(entity.Layer, textLayer, StringComparison.OrdinalIgnoreCase) && IsTextEntity(entity))
                {
                    stats.CheckedTextCount++;
                    string rawText = GetTextString(entity);
                    double value;
                    if (!TryParseStrictNumber(rawText, out value))
                    {
                        logLines.Add("SKIP non-number " + FormatObjectId(id) + ": " + rawText);
                        continue;
                    }

                    TextInfo text = GetTextInfo(entity);
                    if (text == null)
                    {
                        logLines.Add("SKIP no extents " + FormatObjectId(id) + ": " + rawText);
                        continue;
                    }

                    text.Id = id;
                    text.Text = rawText;
                    text.Value = value;
                    texts.Add(text);
                    stats.ParsedNumberCount++;
                }
            }

            logLines.Add("Axis beam segments in range: " + segments.Count);
            logLines.Add("Numeric texts in range: " + texts.Count);

            foreach (TextInfo text in texts)
            {
                MatchInfo match = FindBestMatch(text, segments);
                if (match == null)
                {
                    logLines.Add("NO MATCH text " + FormatObjectId(text.Id) + ": " + text.Text);
                    continue;
                }

                stats.MatchedTextIds.Add(text.Id);
                if (match.Orientation == SegmentOrientation.Horizontal)
                {
                    stats.HorizontalAboveCount++;
                }
                else
                {
                    stats.VerticalLeftCount++;
                }

                logLines.Add(
                    "MATCH text " + FormatObjectId(text.Id) +
                    " value=" + text.Value.ToString(CultureInfo.InvariantCulture) +
                    " -> " + match.Orientation +
                    " segment " + FormatObjectId(match.Segment.Id) +
                    ", gap=" + match.Gap.ToString(CultureInfo.InvariantCulture));
            }

            return stats;
        }

        private static void AddBeamSegments(Entity entity, ObjectId id, List<BeamSegment> segments, List<string> logLines)
        {
            Line line = entity as Line;
            if (line != null)
            {
                AddBeamSegment(id, new Point2d(line.StartPoint.X, line.StartPoint.Y), new Point2d(line.EndPoint.X, line.EndPoint.Y), segments, logLines);
                return;
            }

            Polyline polyline = entity as Polyline;
            if (polyline != null)
            {
                int count = polyline.NumberOfVertices;
                int last = polyline.Closed ? count : count - 1;
                for (int i = 0; i < last; i++)
                {
                    Point2d start = polyline.GetPoint2dAt(i);
                    Point2d end = polyline.GetPoint2dAt((i + 1) % count);
                    if (Math.Abs(polyline.GetBulgeAt(i)) > 1e-9)
                    {
                        continue;
                    }

                    AddBeamSegment(id, start, end, segments, logLines);
                }
            }
        }

        private static void AddBeamSegment(ObjectId id, Point2d start, Point2d end, List<BeamSegment> segments, List<string> logLines)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-9)
            {
                return;
            }

            double axisTolerance = Math.Max(length * 0.02, 1e-8);
            if (Math.Abs(dy) <= axisTolerance)
            {
                segments.Add(new BeamSegment
                {
                    Id = id,
                    Orientation = SegmentOrientation.Horizontal,
                    Fixed = (start.Y + end.Y) / 2.0,
                    Min = Math.Min(start.X, end.X),
                    Max = Math.Max(start.X, end.X),
                    Length = length
                });
                return;
            }

            if (Math.Abs(dx) <= axisTolerance)
            {
                segments.Add(new BeamSegment
                {
                    Id = id,
                    Orientation = SegmentOrientation.Vertical,
                    Fixed = (start.X + end.X) / 2.0,
                    Min = Math.Min(start.Y, end.Y),
                    Max = Math.Max(start.Y, end.Y),
                    Length = length
                });
            }
        }

        private static MatchInfo FindBestMatch(TextInfo text, List<BeamSegment> segments)
        {
            MatchInfo best = null;

            foreach (BeamSegment segment in segments)
            {
                MatchInfo match = IsTextOnBeamNumberSide(text, segment);
                if (match == null)
                {
                    continue;
                }

                if (best == null || match.Gap < best.Gap)
                {
                    best = match;
                }
            }

            return best;
        }

        private static MatchInfo IsTextOnBeamNumberSide(TextInfo text, BeamSegment segment)
        {
            double width = Math.Max(1e-9, Math.Abs(text.Max.X - text.Min.X));
            double height = Math.Max(1e-9, Math.Abs(text.Max.Y - text.Min.Y));

            if (segment.Orientation == SegmentOrientation.Horizontal)
            {
                double xTolerance = Math.Max(width * 0.8, segment.Length * 0.02);
                bool overlapsX = text.Max.X >= segment.Min - xTolerance && text.Min.X <= segment.Max + xTolerance;
                if (!overlapsX || text.Center.Y < segment.Fixed)
                {
                    return null;
                }

                double gap = text.Min.Y - segment.Fixed;
                double minGap = -height * 0.35;
                double maxGap = Math.Max(height * 2.5, segment.Length * 0.015);
                if (gap < minGap || gap > maxGap)
                {
                    return null;
                }

                return new MatchInfo
                {
                    Segment = segment,
                    Orientation = segment.Orientation,
                    Gap = Math.Abs(gap)
                };
            }

            double yTolerance = Math.Max(height * 0.8, segment.Length * 0.02);
            bool overlapsY = text.Max.Y >= segment.Min - yTolerance && text.Min.Y <= segment.Max + yTolerance;
            if (!overlapsY || text.Center.X > segment.Fixed)
            {
                return null;
            }

            double leftGap = segment.Fixed - text.Max.X;
            double minLeftGap = -width * 0.35;
            double maxLeftGap = Math.Max(width * 2.5, segment.Length * 0.015);
            if (leftGap < minLeftGap || leftGap > maxLeftGap)
            {
                return null;
            }

            return new MatchInfo
            {
                Segment = segment,
                Orientation = segment.Orientation,
                Gap = Math.Abs(leftGap)
            };
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

        private static TextInfo GetTextInfo(Entity entity)
        {
            DBText dbText = entity as DBText;
            if (dbText != null)
            {
                return CreateTextInfo(dbText, dbText.Position);
            }

            MText mText = entity as MText;
            if (mText != null)
            {
                return CreateTextInfo(mText, mText.Location);
            }

            return null;
        }

        private static TextInfo CreateTextInfo(Entity entity, Point3d fallbackPoint)
        {
            try
            {
                Extents3d extents = entity.GeometricExtents;
                Point2d center = new Point2d(
                    (extents.MinPoint.X + extents.MaxPoint.X) / 2.0,
                    (extents.MinPoint.Y + extents.MaxPoint.Y) / 2.0);

                return new TextInfo
                {
                    Center = center,
                    Min = new Point2d(extents.MinPoint.X, extents.MinPoint.Y),
                    Max = new Point2d(extents.MaxPoint.X, extents.MaxPoint.Y)
                };
            }
            catch
            {
                Point2d fallback = new Point2d(fallbackPoint.X, fallbackPoint.Y);
                return new TextInfo
                {
                    Center = fallback,
                    Min = fallback,
                    Max = fallback
                };
            }
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

        private static List<ObjectId> CollectLayerEntityIds(Database db, Transaction tr, string layerName)
        {
            List<ObjectId> ids = new List<ObjectId>();
            BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

            foreach (ObjectId id in currentSpace)
            {
                Entity entity = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (entity != null && string.Equals(entity.Layer, layerName, StringComparison.OrdinalIgnoreCase))
                {
                    ids.Add(id);
                }
            }

            return ids;
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

        private enum SegmentOrientation
        {
            Horizontal,
            Vertical
        }

        private class BeamSegment
        {
            public ObjectId Id { get; set; }

            public SegmentOrientation Orientation { get; set; }

            public double Fixed { get; set; }

            public double Min { get; set; }

            public double Max { get; set; }

            public double Length { get; set; }
        }

        private class TextInfo
        {
            public ObjectId Id { get; set; }

            public string Text { get; set; }

            public double Value { get; set; }

            public Point2d Center { get; set; }

            public Point2d Min { get; set; }

            public Point2d Max { get; set; }
        }

        private class MatchInfo
        {
            public BeamSegment Segment { get; set; }

            public SegmentOrientation Orientation { get; set; }

            public double Gap { get; set; }
        }

        private class SelectStats
        {
            public SelectStats()
            {
                MatchedTextIds = new List<ObjectId>();
            }

            public int CheckedTextCount { get; set; }

            public int ParsedNumberCount { get; set; }

            public int HorizontalAboveCount { get; set; }

            public int VerticalLeftCount { get; set; }

            public List<ObjectId> MatchedTextIds { get; private set; }
        }
    }
}
