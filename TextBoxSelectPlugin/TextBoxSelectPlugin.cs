using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace TextBoxSelectPlugin
{
    public class TextBoxSelectCommand : IExtensionApplication
    {
        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "TXTBOXSEL.log");

        public void Initialize()
        {
        }

        public void Terminate()
        {
        }

        [CommandMethod("TXTBOXSEL")]
        public void SelectBoxesContainingText()
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
            logLines.Add("TXTBOXSEL started: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            try
            {
                string boxLayer;
                List<ObjectId> boxLayerIds;
                if (!PromptAndConfirmLayer(db, ed, "\n\u8bf7\u9009\u62e9\u6846\u7ebf\u56fe\u5c42\u4e0a\u7684\u4efb\u610f\u5bf9\u8c61: ", "\n\u662f\u5426\u786e\u8ba4\u4f7f\u7528\u5f53\u524d\u9ad8\u4eae\u7684\u6846\u7ebf\u56fe\u5c42 [\u662f(Y)/\u5426(N)] <Y>: ", out boxLayer, out boxLayerIds))
                {
                    return;
                }

                logLines.Add("Box layer: " + boxLayer + ", entities in current space: " + boxLayerIds.Count);
                highlightedIds.AddRange(boxLayerIds);

                string textLayer;
                List<ObjectId> textLayerIds;
                if (!PromptAndConfirmLayer(db, ed, "\n\u8bf7\u9009\u62e9\u6587\u5b57\u56fe\u5c42\u4e0a\u7684\u4efb\u610f\u6587\u5b57: ", "\n\u662f\u5426\u786e\u8ba4\u4f7f\u7528\u5f53\u524d\u9ad8\u4eae\u7684\u6587\u5b57\u56fe\u5c42 [\u662f(Y)/\u5426(N)] <Y>: ", out textLayer, out textLayerIds))
                {
                    return;
                }

                logLines.Add("Text layer: " + textLayer + ", entities in current space: " + textLayerIds.Count);
                highlightedIds.AddRange(textLayerIds);

                PromptSelectionResult rangeResult = PromptForRange(ed);
                if (rangeResult.Status != PromptStatus.OK)
                {
                    logLines.Add("Range selection cancelled or failed: " + rangeResult.Status);
                    return;
                }

                HashSet<ObjectId> selectedIds = SelectionToSet(rangeResult.Value);
                logLines.Add("Range selection object count: " + selectedIds.Count);
                List<Polyline> boxes = new List<Polyline>();
                List<ObjectId> boxIds = new List<ObjectId>();
                List<TextInfo> texts = new List<TextInfo>();

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in selectedIds)
                    {
                        Entity entity = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                        if (entity == null || entity.IsErased)
                        {
                            continue;
                        }

                        if (string.Equals(entity.Layer, boxLayer, StringComparison.OrdinalIgnoreCase))
                        {
                            Polyline polyline = entity as Polyline;
                            if (polyline != null && polyline.Closed && polyline.NumberOfVertices >= 3)
                            {
                                boxes.Add(polyline);
                                boxIds.Add(id);
                                logLines.Add("Box candidate: " + FormatObjectId(id) + ", area=" + SafeAreaText(polyline));
                            }
                        }
                        else if (string.Equals(entity.Layer, textLayer, StringComparison.OrdinalIgnoreCase))
                        {
                            TextInfo textInfo = GetTextInfo(entity);
                            if (textInfo != null)
                            {
                                textInfo.Id = id;
                                texts.Add(textInfo);
                                logLines.Add("Text candidate: " + FormatObjectId(id) + ", center=(" + textInfo.Center.X + "," + textInfo.Center.Y + ")");
                            }
                        }
                    }

                    logLines.Add("Closed polyline boxes in range: " + boxes.Count);
                    logLines.Add("Text objects in range: " + texts.Count);

                    MatchResult matchResult = FindBoxesContainingText(boxes, boxIds, texts, logLines);
                    List<ObjectId> selectionIds = CombineSelectionIds(matchResult.BoxIds, matchResult.TextIds);
                    logLines.Add("Matched boxes: " + matchResult.BoxIds.Count);
                    logLines.Add("Matched texts: " + matchResult.TextIds.Count);
                    logLines.Add("Combined selection count: " + selectionIds.Count);

                    if (selectionIds.Count > 0)
                    {
                        ed.SetImpliedSelection(selectionIds.ToArray());
                        HighlightEntities(tr, selectionIds, true);
                        highlightedIds.AddRange(selectionIds);
                    }

                    ed.WriteMessage(string.Format("\n\u5b8c\u6210\uff1a\u6846\u9009\u8303\u56f4\u5185\u627e\u5230 {0} \u4e2a\u95ed\u5408 PL \u6846\uff0c{1} \u4e2a\u6587\u5b57\uff0c\u547d\u4e2d {2} \u4e2a\u6846\u548c {3} \u4e2a\u6587\u5b57\uff0c\u5df2\u4e00\u8d77\u9009\u4e2d\u3002", boxes.Count, texts.Count, matchResult.BoxIds.Count, matchResult.TextIds.Count));
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                logLines.Add("ERROR: " + ex.ToString());
                ed.WriteMessage(string.Format("\nTXTBOXSEL \u6267\u884c\u5931\u8d25\uff1a{0}", ex.Message));
            }
            finally
            {
                WriteLog(logLines);
                UnhighlightEntities(db, highlightedIds);
            }
        }

        [CommandMethod("TXTBOXLOG")]
        public void ShowLogPath()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            doc.Editor.WriteMessage("\nTXTBOXSEL log: " + LogPath);
        }

        private static bool PromptAndConfirmLayer(Database db, Editor ed, string pickMessage, string confirmMessage, out string layerName, out List<ObjectId> layerIds)
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
                    ed.WriteMessage("\n\u9009\u62e9\u7684\u5bf9\u8c61\u4e0d\u662f\u6709\u6548\u5b9e\u4f53\u3002");
                    return false;
                }

                layerName = source.Layer;
                layerIds = CollectLayerEntityIds(db, tr, layerName);
                HighlightEntities(tr, layerIds, true);
                tr.Commit();
            }

            ed.WriteMessage(string.Format("\n\u5df2\u9009\u62e9\u56fe\u5c42\uff1a{0}\uff0c\u5f53\u524d\u7a7a\u95f4\u5171\u627e\u5230 {1} \u4e2a\u5bf9\u8c61\u3002", layerName, layerIds.Count));

            PromptKeywordOptions confirmOptions = new PromptKeywordOptions(confirmMessage);
            confirmOptions.Keywords.Add("Y");
            confirmOptions.Keywords.Add("N");
            confirmOptions.Keywords.Default = "Y";
            confirmOptions.AllowNone = true;
            PromptResult confirmResult = ed.GetKeywords(confirmOptions);

            if (confirmResult.Status != PromptStatus.OK || string.Equals(confirmResult.StringResult, "N", StringComparison.OrdinalIgnoreCase))
            {
                UnhighlightEntities(db, layerIds);
                ed.WriteMessage("\n\u5df2\u53d6\u6d88\uff0c\u8bf7\u91cd\u65b0\u6267\u884c TXTBOXSEL\u3002");
                return false;
            }

            return true;
        }

        private static PromptSelectionResult PromptForRange(Editor ed)
        {
            PromptSelectionOptions options = new PromptSelectionOptions();
            options.MessageForAdding = "\n\u8bf7\u6846\u9009/\u4ea4\u53c9\u6846\u9009\u9700\u8981\u8bc6\u522b\u7684\u533a\u57df: ";
            options.AllowDuplicates = false;
            return ed.GetSelection(options);
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

        private static MatchResult FindBoxesContainingText(List<Polyline> boxes, List<ObjectId> boxIds, List<TextInfo> texts, List<string> logLines)
        {
            HashSet<ObjectId> foundBoxes = new HashSet<ObjectId>();
            HashSet<ObjectId> foundTexts = new HashSet<ObjectId>();

            for (int textIndex = 0; textIndex < texts.Count; textIndex++)
            {
                TextInfo text = texts[textIndex];
                int bestBoxIndex = -1;
                double bestArea = double.MaxValue;

                for (int boxIndex = 0; boxIndex < boxes.Count; boxIndex++)
                {
                    Polyline box = boxes[boxIndex];
                    if (!IsTextInsideCandidateBox(box, text, logLines, boxIds[boxIndex]))
                    {
                        continue;
                    }

                    double? area = GetPolylineArea(box);
                    if (!area.HasValue)
                    {
                        continue;
                    }

                    if (area.Value < bestArea)
                    {
                        bestArea = area.Value;
                        bestBoxIndex = boxIndex;
                    }
                }

                if (bestBoxIndex >= 0)
                {
                    foundBoxes.Add(boxIds[bestBoxIndex]);
                    foundTexts.Add(text.Id);
                    logLines.Add("MATCH text " + FormatObjectId(text.Id) + " -> box " + FormatObjectId(boxIds[bestBoxIndex]) + ", area=" + bestArea);
                }
                else
                {
                    logLines.Add("NO MATCH text " + FormatObjectId(text.Id));
                }
            }

            return new MatchResult
            {
                BoxIds = new List<ObjectId>(foundBoxes),
                TextIds = new List<ObjectId>(foundTexts)
            };
        }

        private static List<ObjectId> CombineSelectionIds(IEnumerable<ObjectId> boxIds, IEnumerable<ObjectId> textIds)
        {
            HashSet<ObjectId> combined = new HashSet<ObjectId>();

            foreach (ObjectId id in boxIds)
            {
                combined.Add(id);
            }

            foreach (ObjectId id in textIds)
            {
                combined.Add(id);
            }

            return new List<ObjectId>(combined);
        }

        private static bool IsTextInsideCandidateBox(Polyline box, TextInfo text, List<string> logLines, ObjectId boxId)
        {
            bool centerInsidePolyline = IsPointInsidePolyline(box, text.Center);
            bool centerInsideExtents = IsPointInsideEntityExtents(box, text.Center);

            if (!centerInsidePolyline && !centerInsideExtents)
            {
                return false;
            }

            if (centerInsidePolyline && IsTextExtentsInsideEntityExtents(box, text))
            {
                return true;
            }

            if (centerInsideExtents)
            {
                logLines.Add("FALLBACK text " + FormatObjectId(text.Id) + " accepts box " + FormatObjectId(boxId) + " by extents center");
                return true;
            }

            return false;
        }

        private static bool IsTextExtentsInsideEntityExtents(Entity entity, TextInfo text)
        {
            try
            {
                Extents3d extents = entity.GeometricExtents;
                double textWidth = Math.Abs(text.Max.X - text.Min.X);
                double textHeight = Math.Abs(text.Max.Y - text.Min.Y);
                double tolerance = Math.Max(1e-6, Math.Max(textWidth, textHeight) * 0.15);

                return text.Min.X >= extents.MinPoint.X - tolerance &&
                    text.Min.Y >= extents.MinPoint.Y - tolerance &&
                    text.Max.X <= extents.MaxPoint.X + tolerance &&
                    text.Max.Y <= extents.MaxPoint.Y + tolerance;
            }
            catch
            {
                return true;
            }
        }

        private static bool IsPointInsideEntityExtents(Entity entity, Point2d point)
        {
            try
            {
                Extents3d extents = entity.GeometricExtents;
                return point.X >= extents.MinPoint.X &&
                    point.Y >= extents.MinPoint.Y &&
                    point.X <= extents.MaxPoint.X &&
                    point.Y <= extents.MaxPoint.Y;
            }
            catch
            {
                return false;
            }
        }

        private static double? GetPolylineArea(Polyline polyline)
        {
            try
            {
                double area = Math.Abs(polyline.Area);
                return area > 1e-9 ? area : (double?)null;
            }
            catch
            {
                return null;
            }
        }

        private static string SafeAreaText(Polyline polyline)
        {
            double? area = GetPolylineArea(polyline);
            return area.HasValue ? area.Value.ToString() : "n/a";
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

        private static bool IsPointInsidePolyline(Polyline polyline, Point2d point)
        {
            List<Point2d> vertices = GetSampledPolylinePoints(polyline);
            bool inside = false;
            int count = vertices.Count;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                Point2d pi = vertices[i];
                Point2d pj = vertices[j];

                if (IsPointOnSegment(point, pj, pi))
                {
                    return true;
                }

                bool intersects = ((pi.Y > point.Y) != (pj.Y > point.Y)) &&
                    (point.X < (pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y) + pi.X);

                if (intersects)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static List<Point2d> GetSampledPolylinePoints(Polyline polyline)
        {
            List<Point2d> points = new List<Point2d>();
            int count = polyline.NumberOfVertices;

            for (int i = 0; i < count; i++)
            {
                Point2d start = polyline.GetPoint2dAt(i);
                Point2d end = polyline.GetPoint2dAt((i + 1) % count);
                points.Add(start);

                double bulge = polyline.GetBulgeAt(i);
                if (Math.Abs(bulge) > 1e-9)
                {
                    AddBulgeSamples(points, start, end, bulge);
                }
            }

            return points;
        }

        private static void AddBulgeSamples(List<Point2d> points, Point2d start, Point2d end, double bulge)
        {
            double chord = start.GetDistanceTo(end);
            if (chord < 1e-9)
            {
                return;
            }

            double theta = 4.0 * Math.Atan(bulge);
            int samples = Math.Max(4, Math.Min(48, (int)Math.Ceiling(Math.Abs(theta) / (Math.PI / 18.0))));
            double radius = chord / (2.0 * Math.Sin(Math.Abs(theta) / 2.0));
            double midpointX = (start.X + end.X) / 2.0;
            double midpointY = (start.Y + end.Y) / 2.0;
            double dx = (end.X - start.X) / chord;
            double dy = (end.Y - start.Y) / chord;
            double h = radius * Math.Cos(Math.Abs(theta) / 2.0);
            double sign = bulge >= 0.0 ? 1.0 : -1.0;
            Point2d center = new Point2d(midpointX - sign * dy * h, midpointY + sign * dx * h);
            double startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);

            for (int i = 1; i < samples; i++)
            {
                double angle = startAngle + theta * i / samples;
                points.Add(new Point2d(center.X + Math.Abs(radius) * Math.Cos(angle), center.Y + Math.Abs(radius) * Math.Sin(angle)));
            }
        }

        private static bool IsPointOnSegment(Point2d point, Point2d a, Point2d b)
        {
            double cross = (point.Y - a.Y) * (b.X - a.X) - (point.X - a.X) * (b.Y - a.Y);
            if (Math.Abs(cross) > 1e-7)
            {
                return false;
            }

            double dot = (point.X - a.X) * (b.X - a.X) + (point.Y - a.Y) * (b.Y - a.Y);
            if (dot < 0.0)
            {
                return false;
            }

            double squaredLength = (b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y);
            return dot <= squaredLength;
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

        private class TextInfo
        {
            public ObjectId Id { get; set; }

            public Point2d Center { get; set; }

            public Point2d Min { get; set; }

            public Point2d Max { get; set; }
        }

        private class MatchResult
        {
            public List<ObjectId> BoxIds { get; set; }

            public List<ObjectId> TextIds { get; set; }
        }
    }
}
