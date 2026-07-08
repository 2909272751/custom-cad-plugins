using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(HatchOuterPolylinePlugin.HatchOuterPolylineCommands))]

namespace HatchOuterPolylinePlugin
{
    public class HatchOuterPolylineCommands
    {
        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "HATCHPL.log");
        private const double ArcSegmentLength = 300.0;
        private const double SmallOpeningMaxSize = 600.0;

        [CommandMethod("HATCHPLLOG")]
        public void ShowLog()
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
            ed.WriteMessage("\nHATCHPL 日志: " + LogPath);
        }

        [CommandMethod("HATCHPL")]
        public void CreateOuterPolyline()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            ResetLog();
            Log("HATCHPL started");

            try
            {
                TypedValue[] filter = { new TypedValue((int)DxfCode.Start, "HATCH") };
                PromptSelectionResult sampleRes = ed.GetSelection(
                    new PromptSelectionOptions
                    {
                        MessageForAdding = "\n第一步：选择要处理的 Hatch 样本（可多选，同图层+同填充图案会被处理）: "
                    },
                    new SelectionFilter(filter));

                if (sampleRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n未选择 Hatch 样本。");
                    return;
                }

                PromptSelectionResult selRes = ed.GetSelection(
                    new PromptSelectionOptions
                    {
                        MessageForAdding = "\n第二步：框选要处理的 Hatch 范围（只处理第一步同类型 Hatch）: "
                    },
                    new SelectionFilter(filter));

                if (selRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n未选择 Hatch。");
                    return;
                }

                int created = 0;
                int failed = 0;
                int skippedByType = 0;

                using (DocumentLock docLock = doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord model = (BlockTableRecord)tr.GetObject(
                        SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    LayerTableRecord currentLayer = (LayerTableRecord)tr.GetObject(db.Clayer, OpenMode.ForRead);
                    string currentLayerName = currentLayer.Name;
                    HashSet<string> targetHatchKeys = ReadHatchKeys(tr, sampleRes);
                    if (targetHatchKeys.Count == 0)
                    {
                        ed.WriteMessage("\n样本里没有有效 Hatch。");
                        return;
                    }

                    ed.WriteMessage("\n目标 Hatch 类型: " + DescribeHatchKeys(targetHatchKeys));
                    Log("Target hatch keys=" + DescribeHatchKeys(targetHatchKeys));

                    foreach (SelectedObject so in selRes.Value)
                    {
                        if (so == null || so.ObjectId.IsNull) continue;
                        Hatch hatch = tr.GetObject(so.ObjectId, OpenMode.ForRead, false) as Hatch;
                        if (hatch == null) continue;
                        if (!targetHatchKeys.Contains(GetHatchKey(hatch)))
                        {
                            skippedByType++;
                            Log("Skipped hatch by type id=" + so.ObjectId.ToString() +
                                " key=" + GetHatchKey(hatch));
                            continue;
                        }

                        List<Polyline> outerPolylines = CreateOuterLoopPolylines(hatch, currentLayerName);
                        if (outerPolylines.Count == 0)
                        {
                            failed++;
                            Log("Failed hatch id=" + so.ObjectId.ToString());
                            continue;
                        }

                        foreach (Polyline pl in outerPolylines)
                        {
                            model.AppendEntity(pl);
                            tr.AddNewlyCreatedDBObject(pl, true);
                            created++;
                        }

                    }

                    tr.Commit();
                }

                ed.WriteMessage("\nHATCHPL 完成。生成: " + created.ToString(CultureInfo.InvariantCulture) +
                    "，失败: " + failed.ToString(CultureInfo.InvariantCulture) +
                    "，跳过非目标 Hatch: " + skippedByType.ToString(CultureInfo.InvariantCulture) +
                    "。原 Hatch 未修改。日志: " + LogPath);
                Log("HATCHPL finished created=" + created.ToString(CultureInfo.InvariantCulture) +
                    " failed=" + failed.ToString(CultureInfo.InvariantCulture) +
                    " skippedByType=" + skippedByType.ToString(CultureInfo.InvariantCulture));
            }
            catch (System.Exception ex)
            {
                Log("ERROR: " + ex);
                ed.WriteMessage("\nHATCHPL 出错: " + ex.Message + "\n日志: " + LogPath);
            }
        }

        private static Polyline CreateOuterLoopPolyline(Hatch hatch, string layerName)
        {
            List<Polyline> loops = CreateOuterLoopPolylines(hatch, layerName);
            return loops.Count == 0 ? null : loops[0];
        }

        private static List<Polyline> CreateOuterLoopPolylines(Hatch hatch, string layerName)
        {
            List<LoopCandidate> candidates = new List<LoopCandidate>();
            Polyline best = null;
            double bestArea = 0.0;

            for (int i = 0; i < hatch.NumberOfLoops; i++)
            {
                HatchLoop loop = hatch.GetLoopAt(i);
                bool markedOuter = IsOuterLoop(loop);
                bool textBox = IsTextBoxLoop(loop);
                Polyline candidate = ConvertLoopToPolyline(loop, layerName);
                if (candidate == null || candidate.NumberOfVertices < 3)
                {
                    Log("Skipped loop " + i.ToString(CultureInfo.InvariantCulture) +
                        " type=" + loop.LoopType.ToString());
                    continue;
                }

                double area = Math.Abs(candidate.Area);
                Log("Loop " + i.ToString(CultureInfo.InvariantCulture) +
                    " type=" + loop.LoopType.ToString() +
                    " markedOuter=" + markedOuter.ToString(CultureInfo.InvariantCulture) +
                    " textBox=" + textBox.ToString(CultureInfo.InvariantCulture) +
                    " area=" + area.ToString("0.###", CultureInfo.InvariantCulture) +
                    " vertices=" + candidate.NumberOfVertices.ToString(CultureInfo.InvariantCulture));

                if (!textBox)
                    candidates.Add(new LoopCandidate(candidate, area, i));

                if (area > bestArea)
                {
                    best = candidate;
                    bestArea = area;
                }
            }

            List<Polyline> result = new List<Polyline>();
            if (best == null || candidates.Count == 0)
                return result;

            double minArea = Math.Max(1.0, bestArea * 0.05);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Area < minArea)
                {
                    Log("Ignored small loop index=" + candidates[i].Index.ToString(CultureInfo.InvariantCulture) +
                        " area=" + candidates[i].Area.ToString("0.###", CultureInfo.InvariantCulture));
                    continue;
                }

                bool containedByAnother = false;
                for (int j = 0; j < candidates.Count; j++)
                {
                    if (i == j) continue;
                    if (candidates[j].Area <= candidates[i].Area) continue;
                    if (PolylineContainsPolyline(candidates[j].Polyline, candidates[i].Polyline))
                    {
                        containedByAnother = true;
                        Log("Ignored contained loop index=" + candidates[i].Index.ToString(CultureInfo.InvariantCulture) +
                            " containedBy=" + candidates[j].Index.ToString(CultureInfo.InvariantCulture));
                        break;
                    }
                }

                if (containedByAnother) continue;

                Polyline cleaned = candidates[i].Polyline;
                LogPolylineVertices("Selected loop before cleanup", cleaned);
                cleaned = RemoveSmallDetours(cleaned, layerName);
                cleaned.Closed = true;
                cleaned.Layer = layerName;
                LogPolylineVertices("Selected loop after cleanup", cleaned);
                Log("Selected outer loop area=" + candidates[i].Area.ToString("0.###", CultureInfo.InvariantCulture) +
                    " cleanedArea=" + Math.Abs(cleaned.Area).ToString("0.###", CultureInfo.InvariantCulture));
                result.Add(cleaned);
            }

            return result;
        }

        private static bool IsTextBoxLoop(HatchLoop loop)
        {
            HatchLoopTypes t = loop.LoopType;
            return (t & HatchLoopTypes.Textbox) == HatchLoopTypes.Textbox;
        }

        private static bool PolylineContainsPolyline(Polyline outer, Polyline inner)
        {
            for (int i = 0; i < inner.NumberOfVertices; i++)
            {
                if (!PointInPolyline(outer, inner.GetPoint2dAt(i)))
                    return false;
            }
            return true;
        }

        private static bool PointInPolyline(Polyline pl, Point2d point)
        {
            bool inside = false;
            int count = pl.NumberOfVertices;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                Point2d pi = pl.GetPoint2dAt(i);
                Point2d pj = pl.GetPoint2dAt(j);
                bool intersect = ((pi.Y > point.Y) != (pj.Y > point.Y)) &&
                    (point.X < (pj.X - pi.X) * (point.Y - pi.Y) / ((pj.Y - pi.Y) == 0 ? 1e-12 : (pj.Y - pi.Y)) + pi.X);
                if (intersect) inside = !inside;
            }
            return inside;
        }

        private static Polyline CreateBoundingRectanglePolyline(Hatch hatch, string layerName)
        {
            try
            {
                Extents3d ext = hatch.GeometricExtents;
                Polyline pl = new Polyline();
                pl.AddVertexAt(0, new Point2d(ext.MinPoint.X, ext.MinPoint.Y), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(ext.MaxPoint.X, ext.MinPoint.Y), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(ext.MaxPoint.X, ext.MaxPoint.Y), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(ext.MinPoint.X, ext.MaxPoint.Y), 0, 0, 0);
                pl.Closed = true;
                pl.Layer = layerName;
                Log("Created bounding rectangle min=(" +
                    ext.MinPoint.X.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                    ext.MinPoint.Y.ToString("0.###", CultureInfo.InvariantCulture) + ") max=(" +
                    ext.MaxPoint.X.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                    ext.MaxPoint.Y.ToString("0.###", CultureInfo.InvariantCulture) + ")");
                return pl;
            }
            catch (System.Exception ex)
            {
                Log("Failed to create bounding rectangle: " + ex.Message);
                return null;
            }
        }

        private static HashSet<string> ReadLayers(Transaction tr, PromptSelectionResult selection)
        {
            HashSet<string> layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (selection.Status != PromptStatus.OK) return layers;

            foreach (SelectedObject so in selection.Value)
            {
                if (so == null || so.ObjectId.IsNull) continue;
                Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead, false) as Entity;
                if (ent != null)
                    layers.Add(ent.Layer);
            }
            return layers;
        }

        private static HashSet<string> ReadHatchKeys(Transaction tr, PromptSelectionResult selection)
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (selection.Status != PromptStatus.OK) return keys;

            foreach (SelectedObject so in selection.Value)
            {
                if (so == null || so.ObjectId.IsNull) continue;
                Hatch hatch = tr.GetObject(so.ObjectId, OpenMode.ForRead, false) as Hatch;
                if (hatch == null) continue;
                keys.Add(GetHatchKey(hatch));
            }

            return keys;
        }

        private static string GetHatchKey(Hatch hatch)
        {
            string layer = hatch.Layer ?? "";
            string pattern = "";
            try
            {
                pattern = hatch.PatternName ?? "";
            }
            catch
            {
                pattern = "";
            }

            return layer + "\t" + pattern;
        }

        private static string DescribeHatchKeys(HashSet<string> keys)
        {
            List<string> parts = new List<string>();
            foreach (string key in keys)
            {
                string[] pair = key.Split('\t');
                string layer = pair.Length > 0 ? pair[0] : "";
                string pattern = pair.Length > 1 ? pair[1] : "";
                parts.Add("[图层=" + layer + ", 图案=" + pattern + "]");
            }

            return string.Join(", ", parts.ToArray());
        }

        private static List<ObjectId> GetEntityIdsByLayers(Transaction tr, BlockTableRecord model, HashSet<string> layers)
        {
            List<ObjectId> ids = new List<ObjectId>();
            if (layers.Count == 0) return ids;

            foreach (ObjectId id in model)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null || !layers.Contains(ent.Layer)) continue;
                ids.Add(id);
            }
            return ids;
        }

        private static List<Extents2d> GetEntityExtents(Transaction tr, List<ObjectId> ids)
        {
            List<Extents2d> extents = new List<Extents2d>();

            foreach (ObjectId id in ids)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null) continue;
                try
                {
                    Extents3d e = ent.GeometricExtents;
                    extents.Add(new Extents2d(
                        new Point2d(e.MinPoint.X, e.MinPoint.Y),
                        new Point2d(e.MaxPoint.X, e.MaxPoint.Y)));
                }
                catch
                {
                }
            }
            return extents;
        }

        private static void HighlightObjects(Transaction tr, List<ObjectId> ids, bool highlight)
        {
            foreach (ObjectId id in ids)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null) continue;
                if (highlight) ent.Highlight();
                else ent.Unhighlight();
            }
        }

        private static List<Polyline> CreateBypassPolylinesFromEntities(Hatch hatch, string layerName, List<Extents2d> bypassExtents)
        {
            List<Polyline> result = new List<Polyline>();
            Polyline hatchOuter = CreateOuterLoopPolyline(hatch, layerName);
            if (hatchOuter == null) return result;
            Extents2d hatchExt = GetPolylineExtents(hatchOuter);

            foreach (Extents2d e in bypassExtents)
            {
                if (!Intersects(hatchExt, e)) continue;
                Polyline pl = new Polyline();
                pl.AddVertexAt(0, e.MinPoint, 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(e.MaxPoint.X, e.MinPoint.Y), 0, 0, 0);
                pl.AddVertexAt(2, e.MaxPoint, 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(e.MinPoint.X, e.MaxPoint.Y), 0, 0, 0);
                pl.Closed = true;
                pl.Layer = layerName;
                result.Add(pl);
                Log("Created bypass polyline from selected layer extents min=(" +
                    e.MinPoint.X.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                    e.MinPoint.Y.ToString("0.###", CultureInfo.InvariantCulture) + ") max=(" +
                    e.MaxPoint.X.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                    e.MaxPoint.Y.ToString("0.###", CultureInfo.InvariantCulture) + ")");
            }

            return result;
        }

        private static List<Polyline> CreateBypassLoopPolylines(Hatch hatch, string layerName, List<Extents2d> bypassExtents)
        {
            List<Polyline> result = new List<Polyline>();
            double outerArea = 0.0;
            List<Polyline> candidates = new List<Polyline>();

            for (int i = 0; i < hatch.NumberOfLoops; i++)
            {
                HatchLoop loop = hatch.GetLoopAt(i);
                Polyline candidate = ConvertLoopToPolyline(loop, layerName);
                if (candidate == null || candidate.NumberOfVertices < 3) continue;

                double area = Math.Abs(candidate.Area);
                outerArea = Math.Max(outerArea, area);
                candidates.Add(candidate);
            }

            foreach (Polyline candidate in candidates)
            {
                double area = Math.Abs(candidate.Area);
                if (Math.Abs(area - outerArea) <= Math.Max(1.0, outerArea * 1e-8))
                    continue;

                Extents2d loopExt = GetPolylineExtents(candidate);
                if (IntersectsAny(loopExt, bypassExtents))
                {
                    candidate.Closed = true;
                    candidate.Layer = layerName;
                    result.Add(candidate);
                    Log("Preserved bypass inner loop area=" + area.ToString("0.###", CultureInfo.InvariantCulture));
                }
            }

            return result;
        }

        private static Extents2d GetPolylineExtents(Polyline pl)
        {
            Point2d first = pl.GetPoint2dAt(0);
            double minX = first.X;
            double minY = first.Y;
            double maxX = first.X;
            double maxY = first.Y;

            for (int i = 1; i < pl.NumberOfVertices; i++)
            {
                Point2d p = pl.GetPoint2dAt(i);
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            return new Extents2d(new Point2d(minX, minY), new Point2d(maxX, maxY));
        }

        private static bool IntersectsAny(Extents2d a, List<Extents2d> list)
        {
            foreach (Extents2d b in list)
            {
                if (Intersects(a, b))
                    return true;
            }
            return false;
        }

        private static bool Intersects(Extents2d a, Extents2d b)
        {
            return a.MinPoint.X <= b.MaxPoint.X &&
                   a.MaxPoint.X >= b.MinPoint.X &&
                   a.MinPoint.Y <= b.MaxPoint.Y &&
                   a.MaxPoint.Y >= b.MinPoint.Y;
        }

        private static Polyline RemoveSmallDetours(Polyline source, string layerName)
        {
            List<VertexData> vertices = GetVertices(source);
            if (vertices.Count < 4) return source;

            Extents2d ext = GetExtents(vertices);
            double diag = Math.Sqrt((ext.MaxPoint.X - ext.MinPoint.X) * (ext.MaxPoint.X - ext.MinPoint.X) +
                                    (ext.MaxPoint.Y - ext.MinPoint.Y) * (ext.MaxPoint.Y - ext.MinPoint.Y));
            double closeTol = Math.Max(5.0, diag * 1e-5);
            RemoveClosingDuplicate(vertices, closeTol);
            if (vertices.Count < 4) return source;

            ext = GetExtents(vertices);
            double totalBoxArea = Math.Max(1.0, (ext.MaxPoint.X - ext.MinPoint.X) * (ext.MaxPoint.Y - ext.MinPoint.Y));

            bool changed = true;
            int guard = 0;
            while (changed && guard < 20)
            {
                changed = false;
                guard++;
                for (int i = 0; i < vertices.Count && !changed; i++)
                {
                    for (int j = i + 3; j < vertices.Count && !changed; j++)
                    {
                        if (i == 0 && j == vertices.Count - 1) continue;
                        if (vertices[i].Point.GetDistanceTo(vertices[j].Point) > closeTol) continue;

                        List<VertexData> sub = vertices.GetRange(i + 1, j - i - 1);
                        if (sub.Count == 0) continue;

                        Extents2d subExt = GetExtents(sub);
                        double subW = subExt.MaxPoint.X - subExt.MinPoint.X;
                        double subH = subExt.MaxPoint.Y - subExt.MinPoint.Y;
                        double subBoxArea = Math.Max(0.0, subW * subH);
                        double maxDim = Math.Max(subW, subH);
                        double totalMaxDim = Math.Max(ext.MaxPoint.X - ext.MinPoint.X, ext.MaxPoint.Y - ext.MinPoint.Y);
                        bool smallByUserRule = (subW <= SmallOpeningMaxSize || subH <= SmallOpeningMaxSize) &&
                            maxDim <= Math.Max(SmallOpeningMaxSize * 2.0, totalMaxDim * 0.25);

                        if ((subBoxArea <= totalBoxArea * 0.08 && maxDim <= totalMaxDim * 0.35) || smallByUserRule)
                        {
                            Log("Removed small repeated-point detour: start=" + i.ToString(CultureInfo.InvariantCulture) +
                                " end=" + j.ToString(CultureInfo.InvariantCulture) +
                                " subW=" + subW.ToString("0.###", CultureInfo.InvariantCulture) +
                                " subH=" + subH.ToString("0.###", CultureInfo.InvariantCulture) +
                                " subBoxArea=" + subBoxArea.ToString("0.###", CultureInfo.InvariantCulture));
                            vertices.RemoveRange(i + 1, j - i - 1);
                            vertices[i] = new VertexData(vertices[i].Point, 0.0);
                            changed = true;
                        }
                    }
                }
            }

            changed = true;
            guard = 0;
            while (changed && guard < 40)
            {
                changed = false;
                guard++;

                for (int span = Math.Min(6, vertices.Count - 1); span >= 3 && !changed; span--)
                {
                    for (int i = 0; i < vertices.Count && !changed; i++)
                    {
                        int end = WrapIndex(i + span, vertices.Count);
                        Point2d a = vertices[i].Point;
                        Point2d d = vertices[end].Point;
                        double dx = Math.Abs(a.X - d.X);
                        double dy = Math.Abs(a.Y - d.Y);
                        double axisTol = Math.Max(5.0, diag * 1e-5);
                        bool closesToVerticalEdge = dx <= axisTol;
                        bool closesToHorizontalEdge = dy <= axisTol;
                        if (!closesToVerticalEdge && !closesToHorizontalEdge) continue;

                        List<VertexData> sub = GetCyclicRange(vertices, i + 1, span - 1);
                        if (sub.Count == 0) continue;

                        List<VertexData> boxVertices = new List<VertexData>(sub);
                        boxVertices.Add(vertices[i]);
                        boxVertices.Add(vertices[end]);
                        Extents2d subExt = GetExtents(boxVertices);
                        double subW = subExt.MaxPoint.X - subExt.MinPoint.X;
                        double subH = subExt.MaxPoint.Y - subExt.MinPoint.Y;
                        double smallDim = Math.Min(subW, subH);
                        double maxDim = Math.Max(subW, subH);
                        double subBoxArea = Math.Max(0.0, subW * subH);
                        double totalMaxDim = Math.Max(ext.MaxPoint.X - ext.MinPoint.X, ext.MaxPoint.Y - ext.MinPoint.Y);
                        bool smallByUserRule = smallDim <= SmallOpeningMaxSize &&
                            maxDim <= Math.Max(SmallOpeningMaxSize * 2.0, totalMaxDim * 0.25);

                        if (!smallByUserRule) continue;

                        Log("Removed small edge notch: start=" + i.ToString(CultureInfo.InvariantCulture) +
                            " end=" + end.ToString(CultureInfo.InvariantCulture) +
                            " subW=" + subW.ToString("0.###", CultureInfo.InvariantCulture) +
                            " subH=" + subH.ToString("0.###", CultureInfo.InvariantCulture) +
                            " subBoxArea=" + subBoxArea.ToString("0.###", CultureInfo.InvariantCulture));
                        Point2d anchor = vertices[i].Point;
                        RemoveCyclicRange(vertices, i + 1, span - 1);
                        int anchorIndex = FindVertexIndex(vertices, anchor, axisTol);
                        if (anchorIndex >= 0)
                            vertices[anchorIndex] = new VertexData(vertices[anchorIndex].Point, 0.0);
                        changed = true;
                    }
                }
            }

            return BuildPolyline(vertices, layerName);
        }

        private static void RemoveClosingDuplicate(List<VertexData> vertices, double tolerance)
        {
            if (vertices.Count < 2) return;
            int last = vertices.Count - 1;
            if (vertices[0].Point.GetDistanceTo(vertices[last].Point) <= tolerance)
            {
                Log("Removed duplicate closing vertex");
                vertices.RemoveAt(last);
            }
        }

        private static int WrapIndex(int index, int count)
        {
            int wrapped = index % count;
            return wrapped < 0 ? wrapped + count : wrapped;
        }

        private static List<VertexData> GetCyclicRange(List<VertexData> vertices, int start, int count)
        {
            List<VertexData> result = new List<VertexData>();
            for (int k = 0; k < count; k++)
                result.Add(vertices[WrapIndex(start + k, vertices.Count)]);
            return result;
        }

        private static void RemoveCyclicRange(List<VertexData> vertices, int start, int count)
        {
            List<int> indexes = new List<int>();
            for (int k = 0; k < count; k++)
                indexes.Add(WrapIndex(start + k, vertices.Count));

            indexes.Sort();
            for (int i = indexes.Count - 1; i >= 0; i--)
                vertices.RemoveAt(indexes[i]);
        }

        private static int FindVertexIndex(List<VertexData> vertices, Point2d point, double tolerance)
        {
            for (int i = 0; i < vertices.Count; i++)
            {
                if (vertices[i].Point.GetDistanceTo(point) <= tolerance)
                    return i;
            }
            return -1;
        }

        private static List<VertexData> GetVertices(Polyline pl)
        {
            List<VertexData> vertices = new List<VertexData>();
            for (int i = 0; i < pl.NumberOfVertices; i++)
                vertices.Add(new VertexData(pl.GetPoint2dAt(i), pl.GetBulgeAt(i)));
            return vertices;
        }

        private static Polyline BuildPolyline(List<VertexData> vertices, string layerName)
        {
            Polyline pl = new Polyline();
            for (int i = 0; i < vertices.Count; i++)
                pl.AddVertexAt(i, vertices[i].Point, vertices[i].Bulge, 0, 0);
            pl.Closed = true;
            pl.Layer = layerName;
            return pl;
        }

        private static Extents2d GetExtents(List<VertexData> vertices)
        {
            double minX = vertices[0].Point.X;
            double minY = vertices[0].Point.Y;
            double maxX = vertices[0].Point.X;
            double maxY = vertices[0].Point.Y;
            foreach (VertexData v in vertices)
            {
                minX = Math.Min(minX, v.Point.X);
                minY = Math.Min(minY, v.Point.Y);
                maxX = Math.Max(maxX, v.Point.X);
                maxY = Math.Max(maxY, v.Point.Y);
            }
            return new Extents2d(new Point2d(minX, minY), new Point2d(maxX, maxY));
        }

        private static void LogPolylineVertices(string title, Polyline pl)
        {
            Log(title + " vertices=" + pl.NumberOfVertices.ToString(CultureInfo.InvariantCulture) +
                " area=" + Math.Abs(pl.Area).ToString("0.###", CultureInfo.InvariantCulture));
            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                Point2d p = pl.GetPoint2dAt(i);
                Log("  v" + i.ToString(CultureInfo.InvariantCulture) +
                    "=(" + p.X.ToString("0.###", CultureInfo.InvariantCulture) +
                    "," + p.Y.ToString("0.###", CultureInfo.InvariantCulture) +
                    ") bulge=" + pl.GetBulgeAt(i).ToString("0.######", CultureInfo.InvariantCulture));
            }
        }

        private static bool IsOuterLoop(HatchLoop loop)
        {
            HatchLoopTypes t = loop.LoopType;
            return (t & HatchLoopTypes.External) == HatchLoopTypes.External ||
                   (t & HatchLoopTypes.Outermost) == HatchLoopTypes.Outermost;
        }

        private static Polyline ConvertLoopToPolyline(HatchLoop loop, string layerName)
        {
            if (loop.IsPolyline)
                return ConvertPolylineLoop(loop, layerName);

            return ConvertCurveLoop(loop, layerName);
        }

        private static Polyline ConvertPolylineLoop(HatchLoop loop, string layerName)
        {
            BulgeVertexCollection vertices = loop.Polyline;
            if (vertices == null || vertices.Count < 3) return null;

            Polyline pl = new Polyline();
            for (int i = 0; i < vertices.Count; i++)
            {
                BulgeVertex vertex = vertices[i];
                pl.AddVertexAt(i, vertex.Vertex, vertex.Bulge, 0, 0);
            }
            pl.Closed = true;
            pl.Layer = layerName;
            return pl;
        }

        private static Polyline ConvertCurveLoop(HatchLoop loop, string layerName)
        {
            List<Point2d> points = new List<Point2d>();

            foreach (Curve2d curve in loop.Curves)
            {
                LineSegment2d line = curve as LineSegment2d;
                if (line != null)
                {
                    AddPoint(points, line.StartPoint);
                    AddPoint(points, line.EndPoint);
                    continue;
                }

                CircularArc2d arc = curve as CircularArc2d;
                if (arc != null)
                {
                    AddArcPoints(points, arc);
                    continue;
                }

                Log("Unsupported loop curve type: " + curve.GetType().Name);
            }

            if (points.Count < 3) return null;

            Polyline pl = new Polyline();
            for (int i = 0; i < points.Count; i++)
                pl.AddVertexAt(i, points[i], 0, 0, 0);
            pl.Closed = true;
            pl.Layer = layerName;
            return pl;
        }

        private static void AddArcPoints(List<Point2d> points, CircularArc2d arc)
        {
            double start = arc.StartAngle;
            double end = arc.EndAngle;
            double sweep = end - start;
            if (arc.IsClockWise)
            {
                if (sweep >= 0) sweep -= Math.PI * 2.0;
            }
            else
            {
                if (sweep <= 0) sweep += Math.PI * 2.0;
            }

            double absSweep = Math.Abs(sweep);
            int stepsByAngle = Math.Max(8, (int)Math.Ceiling(absSweep / (Math.PI / 18.0)));
            int stepsByLength = Math.Max(8, (int)Math.Ceiling((arc.Radius * absSweep) / ArcSegmentLength));
            int steps = Math.Min(128, Math.Max(stepsByAngle, stepsByLength));

            for (int i = 0; i <= steps; i++)
            {
                double angle = start + sweep * ((double)i / (double)steps);
                Point2d p = new Point2d(
                    arc.Center.X + Math.Cos(angle) * arc.Radius,
                    arc.Center.Y + Math.Sin(angle) * arc.Radius);
                AddPoint(points, p);
            }
        }

        private static void AddPoint(List<Point2d> points, Point2d point)
        {
            if (points.Count > 0 && points[points.Count - 1].GetDistanceTo(point) < 1e-8)
                return;
            points.Add(point);
        }

        private static void ResetLog()
        {
            File.WriteAllText(LogPath, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "] log started\r\n");
        }

        private static void Log(string message)
        {
            File.AppendAllText(LogPath, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "] " + message + "\r\n");
        }

        private struct VertexData
        {
            public readonly Point2d Point;
            public readonly double Bulge;

            public VertexData(Point2d point, double bulge)
            {
                Point = point;
                Bulge = bulge;
            }
        }

        private struct LoopCandidate
        {
            public readonly Polyline Polyline;
            public readonly double Area;
            public readonly int Index;

            public LoopCandidate(Polyline polyline, double area, int index)
            {
                Polyline = polyline;
                Area = area;
                Index = index;
            }
        }
    }
}
