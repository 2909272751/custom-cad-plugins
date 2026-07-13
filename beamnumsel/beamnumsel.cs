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
        private const string DirectionHorizontal = "H";
        private const string DirectionVertical = "V";
        private const string DirectionAll = "A";
        private const string SideUp = "U";
        private const string SideDown = "D";
        private const string SideLeft = "L";
        private const string SideRight = "R";
        private const string SideBoth = "B";
        private const string ConfirmYes = "Y";
        private const string ConfirmNo = "N";
        private const string ConfirmDistance = "D";
        private const double OrientationToleranceRadians = Math.PI / 6.0;
        private const double DefaultMaxDistance = 50.0;
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
            List<ObjectId> layerHighlightIds = new List<ObjectId>();
            List<ObjectId> previewHighlightIds = new List<ObjectId>();
            List<string> logLines = new List<string>();
            logLines.Add("BEAMNUMSEL v0.2.0 started: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            try
            {
                string beamLayer;
                List<ObjectId> beamLayerIds;
                if (!PromptAndConfirmLayer(db, ed, "\n请选择梁线图层上的一条梁线: ", "\n是否确认使用该梁线图层 [是(Y)/否(N)] <Y>: ", "BEAMNUMSEL", out beamLayer, out beamLayerIds))
                {
                    logLines.Add("Beam layer selection cancelled.");
                    return;
                }

                layerHighlightIds.AddRange(beamLayerIds);
                logLines.Add("Beam layer: " + beamLayer + ", entities in current space: " + beamLayerIds.Count);

                string textLayer;
                List<ObjectId> textLayerIds;
                if (!PromptAndConfirmTextLayer(db, ed, out textLayer, out textLayerIds))
                {
                    logLines.Add("Text layer selection cancelled.");
                    return;
                }

                layerHighlightIds.AddRange(textLayerIds);
                logLines.Add("Text layer: " + textLayer + ", text objects in current space: " + textLayerIds.Count);

                RecognitionOptions options;
                if (!PromptForRecognitionOptions(ed, out options, logLines))
                {
                    return;
                }

                PromptSelectionResult rangeResult = PromptForRange(ed);
                if (rangeResult.Status != PromptStatus.OK)
                {
                    logLines.Add("Range selection cancelled: " + rangeResult.Status);
                    return;
                }

                HashSet<ObjectId> selectedIds = SelectionToSet(rangeResult.Value);
                logLines.Add("Range selection object count: " + selectedIds.Count);

                while (true)
                {
                    UnhighlightEntities(db, previewHighlightIds);
                    previewHighlightIds.Clear();
                    ed.SetImpliedSelection(new ObjectId[0]);

                    SelectStats stats;
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        stats = SelectMatchingTexts(tr, selectedIds, beamLayer, textLayer, options, logLines);
                        if (stats.MatchedTextIds.Count > 0)
                        {
                            ed.SetImpliedSelection(stats.MatchedTextIds.ToArray());
                            HighlightEntities(tr, stats.MatchedTextIds, true);
                            previewHighlightIds.AddRange(stats.MatchedTextIds);
                        }

                        tr.Commit();
                    }

                    ed.WriteMessage(
                        string.Format(
                            "\n完成预览：共检查 {0} 个文字，识别数字 {1} 个，预选 {2} 个。横向 {3} 个，竖向 {4} 个。",
                            stats.CheckedTextCount,
                            stats.ParsedNumberCount,
                            stats.MatchedTextIds.Count,
                            stats.HorizontalCount,
                            stats.VerticalCount));

                    PromptResult confirmResult = PromptForPreviewConfirm(ed);
                    if (confirmResult.Status != PromptStatus.OK)
                    {
                        logLines.Add("Preview confirmation cancelled: " + confirmResult.Status);
                        ed.SetImpliedSelection(new ObjectId[0]);
                        UnhighlightEntities(db, previewHighlightIds);
                        previewHighlightIds.Clear();
                        return;
                    }

                    string confirm = confirmResult.StringResult;
                    if (string.Equals(confirm, ConfirmDistance, StringComparison.OrdinalIgnoreCase))
                    {
                        double newDistance;
                        if (!PromptForMaxDistance(ed, options.MaxDistance, out newDistance))
                        {
                            logLines.Add("Adjust distance cancelled.");
                            return;
                        }

                        options.MaxDistance = newDistance;
                        logLines.Add("Adjusted max distance: " + options.MaxDistance.ToString(CultureInfo.InvariantCulture));
                        continue;
                    }

                    if (string.Equals(confirm, ConfirmNo, StringComparison.OrdinalIgnoreCase))
                    {
                        logLines.Add("Preview rejected.");
                        ed.SetImpliedSelection(new ObjectId[0]);
                        UnhighlightEntities(db, previewHighlightIds);
                        previewHighlightIds.Clear();
                        return;
                    }

                    logLines.Add("Preview accepted. Final selection count: " + stats.MatchedTextIds.Count);
                    return;
                }
            }
            catch (System.Exception ex)
            {
                logLines.Add("ERROR: " + ex.ToString());
                ed.WriteMessage(string.Format("\nBEAMNUMSEL 执行失败：{0}", ex.Message));
            }
            finally
            {
                WriteLog(logLines);
                UnhighlightEntities(db, layerHighlightIds);
                UnhighlightEntities(db, previewHighlightIds);
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

        private static bool PromptForRecognitionOptions(Editor ed, out RecognitionOptions options, List<string> logLines)
        {
            options = new RecognitionOptions();

            PromptResult directionResult = PromptForDirection(ed);
            if (directionResult.Status != PromptStatus.OK)
            {
                logLines.Add("Direction input cancelled: " + directionResult.Status);
                return false;
            }

            options.Direction = directionResult.StringResult;

            if (options.IncludeHorizontal)
            {
                PromptResult horizontalSideResult = PromptForHorizontalSide(ed);
                if (horizontalSideResult.Status != PromptStatus.OK)
                {
                    logLines.Add("Horizontal side input cancelled: " + horizontalSideResult.Status);
                    return false;
                }

                options.HorizontalSide = horizontalSideResult.StringResult;
            }
            else
            {
                options.HorizontalSide = SideBoth;
            }

            if (options.IncludeVertical)
            {
                PromptResult verticalSideResult = PromptForVerticalSide(ed);
                if (verticalSideResult.Status != PromptStatus.OK)
                {
                    logLines.Add("Vertical side input cancelled: " + verticalSideResult.Status);
                    return false;
                }

                options.VerticalSide = verticalSideResult.StringResult;
            }
            else
            {
                options.VerticalSide = SideBoth;
            }

            double maxDistance;
            if (!PromptForMaxDistance(ed, DefaultMaxDistance, out maxDistance))
            {
                logLines.Add("Max distance input cancelled.");
                return false;
            }

            options.MaxDistance = maxDistance;
            logLines.Add("Direction: " + options.Direction + ", horizontal side: " + options.HorizontalSide + ", vertical side: " + options.VerticalSide + ", max distance: " + options.MaxDistance.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        private static PromptResult PromptForDirection(Editor ed)
        {
            PromptKeywordOptions options = new PromptKeywordOptions("\n请选择识别方向 [横向(H)/竖向(V)/全部(A)] <A>: ");
            options.Keywords.Add(DirectionHorizontal);
            options.Keywords.Add(DirectionVertical);
            options.Keywords.Add(DirectionAll);
            options.Keywords.Default = DirectionAll;
            options.AllowNone = true;
            return ed.GetKeywords(options);
        }

        private static PromptResult PromptForHorizontalSide(Editor ed)
        {
            PromptKeywordOptions options = new PromptKeywordOptions("\n横向梁线选择哪侧数字 [上方(U)/下方(D)/两侧(B)] <U>: ");
            options.Keywords.Add(SideUp);
            options.Keywords.Add(SideDown);
            options.Keywords.Add(SideBoth);
            options.Keywords.Default = SideUp;
            options.AllowNone = true;
            return ed.GetKeywords(options);
        }

        private static PromptResult PromptForVerticalSide(Editor ed)
        {
            PromptKeywordOptions options = new PromptKeywordOptions("\n竖向梁线选择哪侧数字 [左侧(L)/右侧(R)/两侧(B)] <L>: ");
            options.Keywords.Add(SideLeft);
            options.Keywords.Add(SideRight);
            options.Keywords.Add(SideBoth);
            options.Keywords.Default = SideLeft;
            options.AllowNone = true;
            return ed.GetKeywords(options);
        }

        private static bool PromptForMaxDistance(Editor ed, double defaultDistance, out double maxDistance)
        {
            maxDistance = defaultDistance;
            PromptDoubleOptions options = new PromptDoubleOptions("\n请输入最大识别距离 <" + defaultDistance.ToString("0.##", CultureInfo.InvariantCulture) + ">: ");
            options.AllowNone = true;
            options.AllowNegative = false;
            options.AllowZero = false;
            PromptDoubleResult result = ed.GetDouble(options);
            if (result.Status == PromptStatus.None)
            {
                return true;
            }

            if (result.Status != PromptStatus.OK)
            {
                return false;
            }

            maxDistance = result.Value;
            return true;
        }

        private static PromptResult PromptForPreviewConfirm(Editor ed)
        {
            PromptKeywordOptions options = new PromptKeywordOptions("\n是否确认选中这些数字 [是(Y)/否(N)/调整距离(D)] <Y>: ");
            options.Keywords.Add(ConfirmYes);
            options.Keywords.Add(ConfirmNo);
            options.Keywords.Add(ConfirmDistance);
            options.Keywords.Default = ConfirmYes;
            options.AllowNone = true;
            return ed.GetKeywords(options);
        }

        private static PromptSelectionResult PromptForRange(Editor ed)
        {
            PromptSelectionOptions options = new PromptSelectionOptions();
            options.MessageForAdding = "\n请框选需要识别的梁和数字范围: ";
            options.AllowDuplicates = false;
            return ed.GetSelection(options);
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
            confirmOptions.Keywords.Add(ConfirmYes);
            confirmOptions.Keywords.Add(ConfirmNo);
            confirmOptions.Keywords.Default = ConfirmYes;
            confirmOptions.AllowNone = true;
            PromptResult confirmResult = ed.GetKeywords(confirmOptions);
            if (confirmResult.Status != PromptStatus.OK || string.Equals(confirmResult.StringResult, ConfirmNo, StringComparison.OrdinalIgnoreCase))
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

            PromptEntityOptions pickOptions = new PromptEntityOptions("\n请选择数字文字图层上的一个文字: ");
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
            confirmOptions.Keywords.Add(ConfirmYes);
            confirmOptions.Keywords.Add(ConfirmNo);
            confirmOptions.Keywords.Default = ConfirmYes;
            confirmOptions.AllowNone = true;
            PromptResult confirmResult = ed.GetKeywords(confirmOptions);
            if (confirmResult.Status != PromptStatus.OK || string.Equals(confirmResult.StringResult, ConfirmNo, StringComparison.OrdinalIgnoreCase))
            {
                UnhighlightEntities(db, textLayerIds);
                ed.WriteMessage("\n已取消，请重新执行 BEAMNUMSEL。");
                return false;
            }

            return true;
        }

        private static SelectStats SelectMatchingTexts(Transaction tr, IEnumerable<ObjectId> selectedIds, string beamLayer, string textLayer, RecognitionOptions options, List<string> logLines)
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
                    AddBeamSegments(entity, id, segments, options, logLines);
                    continue;
                }

                if (string.Equals(entity.Layer, textLayer, StringComparison.OrdinalIgnoreCase) && IsTextEntity(entity))
                {
                    stats.CheckedTextCount++;
                    string rawText = GetTextString(entity);
                    double value;
                    if (!TryParseStrictNumber(rawText, out value))
                    {
                        stats.SkippedNonNumberCount++;
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
                    logLines.Add("TEXT " + FormatObjectId(id) + " value=" + value.ToString(CultureInfo.InvariantCulture) + ", rotation=" + text.Rotation.ToString(CultureInfo.InvariantCulture));
                }
            }

            logLines.Add("Axis beam segments in range: " + segments.Count);
            logLines.Add("Numeric texts in range: " + texts.Count);

            foreach (TextInfo text in texts)
            {
                MatchInfo match = FindBestMatch(text, segments, options, logLines);
                if (match == null)
                {
                    logLines.Add("NO MATCH text " + FormatObjectId(text.Id) + ": " + text.Text);
                    continue;
                }

                stats.MatchedTextIds.Add(text.Id);
                if (match.Segment.Orientation == SegmentOrientation.Horizontal)
                {
                    stats.HorizontalCount++;
                }
                else
                {
                    stats.VerticalCount++;
                }

                logLines.Add(
                    "MATCH text " + FormatObjectId(text.Id) +
                    " value=" + text.Value.ToString(CultureInfo.InvariantCulture) +
                    " -> " + match.Segment.Orientation +
                    " segment " + FormatObjectId(match.Segment.Id) +
                    ", distance=" + match.Distance.ToString(CultureInfo.InvariantCulture) +
                    ", overlap=" + match.ProjectionOverlap.ToString(CultureInfo.InvariantCulture));
            }

            return stats;
        }

        private static void AddBeamSegments(Entity entity, ObjectId id, List<BeamSegment> segments, RecognitionOptions options, List<string> logLines)
        {
            Line line = entity as Line;
            if (line != null)
            {
                AddBeamSegment(id, new Point2d(line.StartPoint.X, line.StartPoint.Y), new Point2d(line.EndPoint.X, line.EndPoint.Y), segments, options);
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

                    Point2d start = polyline.GetPoint2dAt(i);
                    Point2d end = polyline.GetPoint2dAt((i + 1) % count);
                    AddBeamSegment(id, start, end, segments, options);
                }
            }
        }

        private static void AddBeamSegment(ObjectId id, Point2d start, Point2d end, List<BeamSegment> segments, RecognitionOptions options)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < Math.Max(1e-6, options.MaxDistance * 1.2))
            {
                return;
            }

            double axisTolerance = Math.Max(length * 0.02, 1e-8);
            if (Math.Abs(dy) <= axisTolerance && options.IncludeHorizontal)
            {
                segments.Add(new BeamSegment
                {
                    Id = id,
                    Orientation = SegmentOrientation.Horizontal,
                    Axis = (start.Y + end.Y) / 2.0,
                    Min = Math.Min(start.X, end.X),
                    Max = Math.Max(start.X, end.X),
                    Length = length
                });
                return;
            }

            if (Math.Abs(dx) <= axisTolerance && options.IncludeVertical)
            {
                segments.Add(new BeamSegment
                {
                    Id = id,
                    Orientation = SegmentOrientation.Vertical,
                    Axis = (start.X + end.X) / 2.0,
                    Min = Math.Min(start.Y, end.Y),
                    Max = Math.Max(start.Y, end.Y),
                    Length = length
                });
            }
        }

        private static MatchInfo FindBestMatch(TextInfo text, List<BeamSegment> segments, RecognitionOptions options, List<string> logLines)
        {
            MatchInfo best = null;

            foreach (BeamSegment segment in segments)
            {
                MatchInfo match = TryMatchTextToSegment(text, segment, options);
                if (match == null)
                {
                    continue;
                }

                if (best == null ||
                    match.Distance < best.Distance - 1e-9 ||
                    (Math.Abs(match.Distance - best.Distance) <= 1e-9 && match.ProjectionOverlap > best.ProjectionOverlap))
                {
                    best = match;
                }
            }

            return best;
        }

        private static MatchInfo TryMatchTextToSegment(TextInfo text, BeamSegment segment, RecognitionOptions options)
        {
            double width = Math.Max(1e-9, Math.Abs(text.Max.X - text.Min.X));
            double height = Math.Max(1e-9, Math.Abs(text.Max.Y - text.Min.Y));

            if (segment.Orientation == SegmentOrientation.Horizontal)
            {
                if (!IsHorizontalText(text))
                {
                    return null;
                }

                double overlap = GetOverlap(text.Min.X, text.Max.X, segment.Min, segment.Max);
                double minOverlap = Math.Min(width * 0.35, segment.Length * 0.25);
                double endTolerance = Math.Max(width * 0.7, options.MaxDistance * 0.5);
                if (overlap < minOverlap && !IsIntervalNear(text.Min.X, text.Max.X, segment.Min, segment.Max, endTolerance))
                {
                    return null;
                }

                double distance;
                string side = GetHorizontalSide(text, segment.Axis, out distance);
                if (!IsAllowedHorizontalSide(side, options.HorizontalSide) || distance > options.MaxDistance)
                {
                    return null;
                }

                return new MatchInfo
                {
                    Segment = segment,
                    Distance = distance,
                    ProjectionOverlap = overlap
                };
            }

            if (!IsVerticalText(text))
            {
                return null;
            }

            double verticalOverlap = GetOverlap(text.Min.Y, text.Max.Y, segment.Min, segment.Max);
            double minVerticalOverlap = Math.Min(height * 0.35, segment.Length * 0.25);
            double verticalEndTolerance = Math.Max(height * 0.7, options.MaxDistance * 0.5);
            if (verticalOverlap < minVerticalOverlap && !IsIntervalNear(text.Min.Y, text.Max.Y, segment.Min, segment.Max, verticalEndTolerance))
            {
                return null;
            }

            double sideDistance;
            string verticalSide = GetVerticalSide(text, segment.Axis, out sideDistance);
            if (!IsAllowedVerticalSide(verticalSide, options.VerticalSide) || sideDistance > options.MaxDistance)
            {
                return null;
            }

            return new MatchInfo
            {
                Segment = segment,
                Distance = sideDistance,
                ProjectionOverlap = verticalOverlap
            };
        }

        private static string GetHorizontalSide(TextInfo text, double axis, out double distance)
        {
            if (text.Center.Y >= axis)
            {
                distance = Math.Max(0.0, text.Min.Y - axis);
                return SideUp;
            }

            distance = Math.Max(0.0, axis - text.Max.Y);
            return SideDown;
        }

        private static string GetVerticalSide(TextInfo text, double axis, out double distance)
        {
            if (text.Center.X <= axis)
            {
                distance = Math.Max(0.0, axis - text.Max.X);
                return SideLeft;
            }

            distance = Math.Max(0.0, text.Min.X - axis);
            return SideRight;
        }

        private static bool IsAllowedHorizontalSide(string actualSide, string allowedSide)
        {
            return string.Equals(allowedSide, SideBoth, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(actualSide, allowedSide, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAllowedVerticalSide(string actualSide, string allowedSide)
        {
            return string.Equals(allowedSide, SideBoth, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(actualSide, allowedSide, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHorizontalText(TextInfo text)
        {
            double angle = NormalizeAngleToHalfTurn(text.Rotation);
            if (angle <= OrientationToleranceRadians || Math.Abs(Math.PI - angle) <= OrientationToleranceRadians)
            {
                return true;
            }

            double width = Math.Abs(text.Max.X - text.Min.X);
            double height = Math.Abs(text.Max.Y - text.Min.Y);
            return width >= height * 1.25;
        }

        private static bool IsVerticalText(TextInfo text)
        {
            double angle = NormalizeAngleToHalfTurn(text.Rotation);
            if (Math.Abs((Math.PI / 2.0) - angle) <= OrientationToleranceRadians)
            {
                return true;
            }

            double width = Math.Abs(text.Max.X - text.Min.X);
            double height = Math.Abs(text.Max.Y - text.Min.Y);
            return height >= width * 1.25;
        }

        private static double NormalizeAngleToHalfTurn(double angle)
        {
            double result = angle % Math.PI;
            if (result < 0.0)
            {
                result += Math.PI;
            }

            return result;
        }

        private static double GetOverlap(double minA, double maxA, double minB, double maxB)
        {
            return Math.Max(0.0, Math.Min(maxA, maxB) - Math.Max(minA, minB));
        }

        private static bool IsIntervalNear(double minA, double maxA, double minB, double maxB, double tolerance)
        {
            return maxA >= minB - tolerance && minA <= maxB + tolerance;
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
                return CreateTextInfo(dbText, dbText.Position, dbText.Rotation);
            }

            MText mText = entity as MText;
            if (mText != null)
            {
                return CreateTextInfo(mText, mText.Location, mText.Rotation);
            }

            return null;
        }

        private static TextInfo CreateTextInfo(Entity entity, Point3d fallbackPoint, double rotation)
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
                    Max = new Point2d(extents.MaxPoint.X, extents.MaxPoint.Y),
                    Rotation = rotation
                };
            }
            catch
            {
                Point2d fallback = new Point2d(fallbackPoint.X, fallbackPoint.Y);
                return new TextInfo
                {
                    Center = fallback,
                    Min = fallback,
                    Max = fallback,
                    Rotation = rotation
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

        private class RecognitionOptions
        {
            public string Direction { get; set; }

            public string HorizontalSide { get; set; }

            public string VerticalSide { get; set; }

            public double MaxDistance { get; set; }

            public bool IncludeHorizontal
            {
                get
                {
                    return string.Equals(Direction, DirectionHorizontal, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Direction, DirectionAll, StringComparison.OrdinalIgnoreCase);
                }
            }

            public bool IncludeVertical
            {
                get
                {
                    return string.Equals(Direction, DirectionVertical, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Direction, DirectionAll, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private class BeamSegment
        {
            public ObjectId Id { get; set; }

            public SegmentOrientation Orientation { get; set; }

            public double Axis { get; set; }

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

            public double Rotation { get; set; }
        }

        private class MatchInfo
        {
            public BeamSegment Segment { get; set; }

            public double Distance { get; set; }

            public double ProjectionOverlap { get; set; }
        }

        private class SelectStats
        {
            public SelectStats()
            {
                MatchedTextIds = new List<ObjectId>();
            }

            public int CheckedTextCount { get; set; }

            public int ParsedNumberCount { get; set; }

            public int SkippedNonNumberCount { get; set; }

            public int HorizontalCount { get; set; }

            public int VerticalCount { get; set; }

            public List<ObjectId> MatchedTextIds { get; private set; }
        }
    }
}
