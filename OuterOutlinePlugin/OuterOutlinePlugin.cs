using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(OuterOutlinePlugin.OuterCommands))]

namespace OuterOutlinePlugin
{
    public class OuterCommands
    {
        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "PCOUTLINE.log");
        private const double DefaultTolerance = 20.0;

        [CommandMethod("PCLOG")]
        public void ShowLog()
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
            ed.WriteMessage("\nPCOUTLINE 日志: " + LogPath);
        }

        [CommandMethod("PCOUTLINE")]
        public void TraceOuterOutline()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            ResetLog();
            Log("PCOUTLINE started");

            try
            {
                PromptSelectionResult areaRes = ed.GetSelection(
                    new PromptSelectionOptions
                    {
                        MessageForAdding = "\n框选需要提取外轮廓的楼层范围: "
                    });
                if (areaRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n已取消。");
                    return;
                }

                double tol = AskDouble(ed, "\n端点吸附/断口容差，单位按当前图纸 <20>: ", DefaultTolerance);
                string resultLayer = AskString(ed, "\n生成外轮廓图层 <PC_外轮廓>: ", "PC_外轮廓");
                string failLayer = "PC_失败标记";

                HashSet<string> boundaryLayers = PickBoundaryLayersInteractive(ed, db, areaRes.Value, null);
                if (boundaryLayers.Count == 0)
                {
                    ed.WriteMessage("\n没有选择外轮廓候选图层。");
                    return;
                }

                bool allLarge = false;

                int created = 0;
                using (DocumentLock docLock = doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, resultLayer, 4);
                    EnsureLayer(db, tr, failLayer, 1);

                    List<ObjectId> boundaryIds = FilterSelectionByLayers(tr, areaRes.Value, boundaryLayers);
                    Log("PCOUTLINE layers=" + string.Join(", ", new List<string>(boundaryLayers).ToArray()));
                    Log("PCOUTLINE boundary entities=" + boundaryIds.Count.ToString(CultureInfo.InvariantCulture));

                    List<Segment2> rawSegments = ExtractSegments(tr, boundaryIds, tol);
                    Log("PCOUTLINE raw segment count=" + rawSegments.Count.ToString(CultureInfo.InvariantCulture));
                    List<Segment2> splitSegments = SplitSegments(rawSegments, tol);
                    Log("PCOUTLINE split segment count=" + splitSegments.Count.ToString(CultureInfo.InvariantCulture));
                    Graph graph = BuildGraph(splitSegments, tol);
                    Log("PCOUTLINE graph vertices=" + graph.Vertices.Count.ToString(CultureInfo.InvariantCulture) +
                        ", edges=" + graph.UndirectedEdges.Count.ToString(CultureInfo.InvariantCulture));
                    List<List<int>> faces = EnumerateFaces(graph);
                    Log("PCOUTLINE faces=" + faces.Count.ToString(CultureInfo.InvariantCulture));

                    BlockTableRecord model = (BlockTableRecord)tr.GetObject(
                        SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                    List<List<int>> outlines = SelectOuterFaces(graph, faces, allLarge);
                    if (outlines.Count == 0)
                    {
                        Point3d marker = GetSelectionCenter(tr, areaRes.Value);
                        AddFailureMarker(model, tr, failLayer, marker, graph.UndirectedEdges.Count < 4 ? "边界线不足" : "未找到外轮廓");
                    }
                    else
                    {
                        foreach (List<int> face in outlines)
                        {
                            Polyline outline = CreatePolyline(graph, face, resultLayer);
                            model.AppendEntity(outline);
                            tr.AddNewlyCreatedDBObject(outline, true);
                            created++;
                            Log("PCOUTLINE created outline area=" + Math.Abs(PolygonArea(graph, face)).ToString("0.###", CultureInfo.InvariantCulture));
                        }
                    }

                    tr.Commit();
                }

                ed.WriteMessage("\nPCOUTLINE 完成。生成外轮廓: " + created.ToString(CultureInfo.InvariantCulture) +
                    "。日志: " + LogPath);
                Log("PCOUTLINE finished created=" + created.ToString(CultureInfo.InvariantCulture));
            }
            catch (System.Exception ex)
            {
                Log("PCOUTLINE ERROR: " + ex);
                ed.WriteMessage("\nPCOUTLINE 出错: " + ex.Message + "\n日志: " + LogPath);
            }
        }

        private static double AskDouble(Editor ed, string message, double defaultValue)
        {
            PromptDoubleOptions opt = new PromptDoubleOptions(message);
            opt.AllowNone = true;
            opt.AllowNegative = false;
            opt.AllowZero = false;
            PromptDoubleResult res = ed.GetDouble(opt);
            return res.Status == PromptStatus.OK ? res.Value : defaultValue;
        }

        private static string AskString(Editor ed, string message, string defaultValue)
        {
            PromptStringOptions opt = new PromptStringOptions(message);
            opt.AllowSpaces = true;
            PromptResult res = ed.GetString(opt);
            if (res.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(res.StringResult))
                return res.StringResult.Trim();
            return defaultValue;
        }

        private static void EnsureLayer(Database db, Transaction tr, string name, short color)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;
            lt.UpgradeOpen();
            LayerTableRecord layer = new LayerTableRecord();
            layer.Name = name;
            layer.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                Autodesk.AutoCAD.Colors.ColorMethod.ByAci, color);
            lt.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
        }

        private static void HighlightObjects(Database db, List<ObjectId> ids, bool highlight)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in ids)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                    if (ent == null) continue;
                    if (highlight) ent.Highlight();
                    else ent.Unhighlight();
                }
                tr.Commit();
            }
        }

        private static List<ObjectId> ReviewObjectList(Editor ed, Database db, List<ObjectId> initial, string title, string addPrompt, string removePrompt)
        {
            List<ObjectId> current = new List<ObjectId>(initial);
            while (true)
            {
                HighlightObjects(db, current, true);
                Application.UpdateScreen();
                ed.WriteMessage(title + "，当前数量: " + current.Count.ToString(CultureInfo.InvariantCulture));

                PromptKeywordOptions opt = new PromptKeywordOptions("\n修正选择 [Done/Add/Remove] <Done>: ");
                opt.AllowNone = true;
                opt.Keywords.Add("Done");
                opt.Keywords.Add("Add");
                opt.Keywords.Add("Remove");
                PromptResult res = ed.GetKeywords(opt);
                if (res.Status == PromptStatus.None || (res.Status == PromptStatus.OK && res.StringResult == "Done"))
                {
                    HighlightObjects(db, current, false);
                    return current;
                }

                if (res.Status != PromptStatus.OK)
                {
                    HighlightObjects(db, current, false);
                    return current;
                }

                if (res.StringResult == "Add")
                {
                    PromptSelectionResult addRes = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = addPrompt, AllowDuplicates = false });
                    if (addRes.Status == PromptStatus.OK)
                    {
                        HashSet<ObjectId> set = new HashSet<ObjectId>(current);
                        foreach (SelectedObject so in addRes.Value)
                        {
                            if (so != null && !so.ObjectId.IsNull && set.Add(so.ObjectId))
                                current.Add(so.ObjectId);
                        }
                    }
                    continue;
                }

                if (res.StringResult == "Remove")
                {
                    PromptSelectionResult removeRes = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = removePrompt, AllowDuplicates = false });
                    if (removeRes.Status == PromptStatus.OK)
                    {
                        HashSet<ObjectId> remove = new HashSet<ObjectId>();
                        foreach (SelectedObject so in removeRes.Value)
                        {
                            if (so != null && !so.ObjectId.IsNull)
                                remove.Add(so.ObjectId);
                        }
                        HighlightObjects(db, current, false);
                        current.RemoveAll(id => remove.Contains(id));
                    }
                }
            }
        }

        private static HashSet<string> PickBoundaryLayersInteractive(Editor ed, Database db, SelectionSet areaSelection, string symbolLayer)
        {
            HashSet<string> layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<ObjectId> highlighted = new List<ObjectId>();

            while (true)
            {
                PromptEntityOptions entOpt = new PromptEntityOptions(
                    "\n点选边界图层样本对象继续添加，或按 Space/Enter 完成 [Remove]: ");
                entOpt.AllowNone = true;
                entOpt.Keywords.Add("Remove");
                PromptEntityResult entRes = ed.GetEntity(entOpt);

                if (entRes.Status == PromptStatus.None)
                {
                    HighlightObjects(db, highlighted, false);
                    return layers;
                }

                if (entRes.Status == PromptStatus.Keyword && entRes.StringResult == "Remove")
                {
                    PromptEntityOptions removeOpt = new PromptEntityOptions("\n点选要移除的边界图层样本对象: ");
                    PromptEntityResult removeRes = ed.GetEntity(removeOpt);
                    if (removeRes.Status != PromptStatus.OK)
                        continue;

                    string removeLayer = GetEntityLayer(db, removeRes.ObjectId);
                    if (string.IsNullOrEmpty(removeLayer))
                        continue;

                    layers.Remove(removeLayer);
                    ed.WriteMessage("\n已移除边界图层: " + removeLayer);
                    RefreshBoundaryLayerHighlight(ed, db, areaSelection, layers, ref highlighted);
                    continue;
                }

                if (entRes.Status != PromptStatus.OK)
                    continue;

                string layer = GetEntityLayer(db, entRes.ObjectId);
                if (string.IsNullOrEmpty(layer))
                    continue;

                if (layers.Add(layer))
                {
                    ed.WriteMessage("\n已加入边界图层: " + layer);
                    if (StringComparer.OrdinalIgnoreCase.Equals(layer, symbolLayer))
                        ed.WriteMessage("\n注意：这个图层是开洞符号图层，通常不应作为边界图层。");
                }
                else
                {
                    ed.WriteMessage("\n该图层已在边界图层中: " + layer);
                }

                RefreshBoundaryLayerHighlight(ed, db, areaSelection, layers, ref highlighted);
            }
        }

        private static void RefreshBoundaryLayerHighlight(Editor ed, Database db, SelectionSet areaSelection, HashSet<string> layers, ref List<ObjectId> highlighted)
        {
            HighlightObjects(db, highlighted, false);
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                highlighted = FilterSelectionByLayers(tr, areaSelection, layers);
                tr.Commit();
            }
            HighlightObjects(db, highlighted, true);
            ed.WriteMessage("\n当前参与围合图层: " + (layers.Count == 0 ? "(无)" : string.Join(", ", new List<string>(layers).ToArray())));
            ed.WriteMessage("\n当前高亮对象数量: " + highlighted.Count.ToString(CultureInfo.InvariantCulture));
            Application.UpdateScreen();
        }

        private static string GetEntityLayer(Database db, ObjectId id)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                string layer = ent == null ? null : ent.Layer;
                tr.Commit();
                return layer;
            }
        }

        private static HashSet<string> ReadLayersFromSelection(Database db, SelectionSet ss)
        {
            HashSet<string> layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in ss)
                {
                    if (so == null || so.ObjectId.IsNull) continue;
                    Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead, false) as Entity;
                    if (ent == null) continue;
                    layers.Add(ent.Layer);
                }
                tr.Commit();
            }
            return layers;
        }

        private static List<ObjectId> CreatePreviewBoxes(Database db, List<ObjectId> ids)
        {
            List<ObjectId> created = new List<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayer(db, tr, "DK_OPENING_SYMBOL_PREVIEW", 2);
                BlockTableRecord model = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                foreach (ObjectId id in ids)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                    if (ent == null) continue;
                    try
                    {
                        Extents3d ext = ent.GeometricExtents;
                        double w = Math.Max(100.0, ext.MaxPoint.X - ext.MinPoint.X);
                        double h = Math.Max(100.0, ext.MaxPoint.Y - ext.MinPoint.Y);
                        double pad = Math.Max(80.0, Math.Max(w, h) * 0.25);

                        double minX = ext.MinPoint.X - pad;
                        double minY = ext.MinPoint.Y - pad;
                        double maxX = ext.MaxPoint.X + pad;
                        double maxY = ext.MaxPoint.Y + pad;

                        Polyline box = new Polyline();
                        box.AddVertexAt(0, new Point2d(minX, minY), 0, 0, 0);
                        box.AddVertexAt(1, new Point2d(maxX, minY), 0, 0, 0);
                        box.AddVertexAt(2, new Point2d(maxX, maxY), 0, 0, 0);
                        box.AddVertexAt(3, new Point2d(minX, maxY), 0, 0, 0);
                        box.Closed = true;
                        box.Layer = "DK_OPENING_SYMBOL_PREVIEW";
                        box.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                            Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 2);
                        model.AppendEntity(box);
                        tr.AddNewlyCreatedDBObject(box, true);
                        created.Add(box.ObjectId);
                    }
                    catch
                    {
                        Log("Could not create preview box for symbol " + id.ToString());
                    }
                }
                tr.Commit();
            }
            return created;
        }

        private static void DeleteObjects(Database db, List<ObjectId> ids)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in ids)
                {
                    if (id.IsNull || id.IsErased) continue;
                    DBObject obj = tr.GetObject(id, OpenMode.ForWrite, false);
                    if (obj != null) obj.Erase();
                }
                tr.Commit();
            }
        }

        private static bool TryEntityCenter(Entity ent, out Point2 center)
        {
            center = new Point2();
            try
            {
                Extents3d ext = ent.GeometricExtents;
                center = new Point2((ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static List<ObjectId> FilterSelectionByLayers(Transaction tr, SelectionSet ss, HashSet<string> layers)
        {
            List<ObjectId> ids = new List<ObjectId>();
            foreach (SelectedObject so in ss)
            {
                if (so == null || so.ObjectId.IsNull) continue;
                Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead, false) as Entity;
                if (ent == null) continue;
                if (layers.Contains(ent.Layer))
                    ids.Add(so.ObjectId);
            }
            return ids;
        }

        private static List<Segment2> ExtractSegments(Transaction tr, List<ObjectId> ids, double tol)
        {
            List<Segment2> segments = new List<Segment2>();
            foreach (ObjectId id in ids)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null) continue;

                Line line = ent as Line;
                if (line != null)
                {
                    AddSegment(segments, new Point2(line.StartPoint.X, line.StartPoint.Y),
                        new Point2(line.EndPoint.X, line.EndPoint.Y), id, tol);
                    continue;
                }

                Polyline pl = ent as Polyline;
                if (pl != null)
                {
                    int count = pl.NumberOfVertices;
                    int limit = pl.Closed ? count : count - 1;
                    for (int i = 0; i < limit; i++)
                    {
                        int j = (i + 1) % count;
                        Point2d a = pl.GetPoint2dAt(i);
                        Point2d b = pl.GetPoint2dAt(j);
                        if (pl.GetSegmentType(i) == SegmentType.Line)
                        {
                            AddSegment(segments, new Point2(a.X, a.Y), new Point2(b.X, b.Y), id, tol);
                            continue;
                        }
                        if (pl.GetSegmentType(i) == SegmentType.Arc)
                        {
                            AddBulgeArcSegments(segments, new Point2(a.X, a.Y), new Point2(b.X, b.Y), pl.GetBulgeAt(i), id, tol);
                            continue;
                        }
                        Log("Skipped unsupported polyline segment on layer " + pl.Layer);
                    }
                    continue;
                }

                Arc arc = ent as Arc;
                if (arc != null)
                {
                    AddArcSegments(segments, new Point2(arc.Center.X, arc.Center.Y), arc.Radius, arc.StartAngle, arc.EndAngle, id, tol);
                    continue;
                }

                Hatch hatch = ent as Hatch;
                if (hatch != null)
                {
                    AddHatchSegments(segments, hatch, id, tol);
                    continue;
                }

                Log("Skipped unsupported entity type " + ent.GetType().Name + " layer=" + ent.Layer);
            }
            return segments;
        }

        private static void AddHatchSegments(List<Segment2> segments, Hatch hatch, ObjectId source, double tol)
        {
            try
            {
                for (int loopIndex = 0; loopIndex < hatch.NumberOfLoops; loopIndex++)
                {
                    HatchLoop loop = hatch.GetLoopAt(loopIndex);
                    if (loop.IsPolyline)
                    {
                        BulgeVertexCollection vertices = loop.Polyline;
                        int count = vertices.Count;
                        for (int i = 0; i < count; i++)
                        {
                            int j = (i + 1) % count;
                            BulgeVertex va = vertices[i];
                            BulgeVertex vb = vertices[j];
                            Point2 a = new Point2(va.Vertex.X, va.Vertex.Y);
                            Point2 b = new Point2(vb.Vertex.X, vb.Vertex.Y);
                            if (Math.Abs(va.Bulge) < 1e-9)
                                AddSegment(segments, a, b, source, tol);
                            else
                                AddBulgeArcSegments(segments, a, b, va.Bulge, source, tol);
                        }
                    }
                    else
                    {
                        foreach (Curve2d curve in loop.Curves)
                        {
                            LineSegment2d line = curve as LineSegment2d;
                            if (line != null)
                            {
                                AddSegment(segments,
                                    new Point2(line.StartPoint.X, line.StartPoint.Y),
                                    new Point2(line.EndPoint.X, line.EndPoint.Y),
                                    source,
                                    tol);
                                continue;
                            }

                            CircularArc2d arc = curve as CircularArc2d;
                            if (arc != null)
                            {
                                AddArcSegments(segments,
                                    new Point2(arc.Center.X, arc.Center.Y),
                                    arc.Radius,
                                    arc.StartAngle,
                                    arc.EndAngle,
                                    source,
                                    tol);
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log("Skipped hatch boundary on layer " + hatch.Layer + ": " + ex.Message);
            }
        }

        private static void AddSegment(List<Segment2> segments, Point2 a, Point2 b, ObjectId source, double tol)
        {
            if (a.DistanceTo(b) <= tol * 0.1) return;
            segments.Add(new Segment2(a, b, source));
        }

        private static void AddArcSegments(List<Segment2> segments, Point2 center, double radius, double startAngle, double endAngle, ObjectId source, double tol)
        {
            double sweep = endAngle - startAngle;
            if (sweep <= 0) sweep += Math.PI * 2.0;
            AddArcSweepSegments(segments, center, radius, startAngle, sweep, source, tol);
        }

        private static void AddBulgeArcSegments(List<Segment2> segments, Point2 start, Point2 end, double bulge, ObjectId source, double tol)
        {
            if (Math.Abs(bulge) < 1e-9)
            {
                AddSegment(segments, start, end, source, tol);
                return;
            }

            Point2 chord = end - start;
            double c = start.DistanceTo(end);
            if (c <= tol * 0.1) return;
            double theta = 4.0 * Math.Atan(bulge);
            double radius = Math.Abs(c / (2.0 * Math.Sin(Math.Abs(theta) / 2.0)));
            Point2 mid = new Point2((start.X + end.X) / 2.0, (start.Y + end.Y) / 2.0);
            Point2 left = new Point2(-chord.Y / c, chord.X / c);
            double h = c / (2.0 * Math.Tan(theta / 2.0));
            Point2 center = mid + left * h;
            double startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
            AddArcSweepSegments(segments, center, radius, startAngle, theta, source, tol);
        }

        private static void AddArcSweepSegments(List<Segment2> segments, Point2 center, double radius, double startAngle, double sweep, ObjectId source, double tol)
        {
            double absSweep = Math.Abs(sweep);
            int stepsByAngle = Math.Max(4, (int)Math.Ceiling(absSweep / (Math.PI / 18.0)));
            int stepsByLength = Math.Max(4, (int)Math.Ceiling((radius * absSweep) / Math.Max(300.0, tol * 5.0)));
            int steps = Math.Min(96, Math.Max(stepsByAngle, stepsByLength));
            Point2 prev = ArcPoint(center, radius, startAngle);
            for (int i = 1; i <= steps; i++)
            {
                double a = startAngle + sweep * ((double)i / (double)steps);
                Point2 next = ArcPoint(center, radius, a);
                AddSegment(segments, prev, next, source, tol);
                prev = next;
            }
        }

        private static Point2 ArcPoint(Point2 center, double radius, double angle)
        {
            return new Point2(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
        }

        private static List<Segment2> SplitSegments(List<Segment2> source, double tol)
        {
            List<List<double>> ts = new List<List<double>>();
            for (int i = 0; i < source.Count; i++)
                ts.Add(new List<double> { 0.0, 1.0 });

            for (int i = 0; i < source.Count; i++)
            {
                for (int j = i + 1; j < source.Count; j++)
                {
                    double ti, tj;
                    if (TryIntersect(source[i], source[j], tol, out ti, out tj))
                    {
                        AddT(ts[i], ti, tol, source[i].Length);
                        AddT(ts[j], tj, tol, source[j].Length);
                    }
                }
            }

            List<Segment2> result = new List<Segment2>();
            for (int i = 0; i < source.Count; i++)
            {
                ts[i].Sort();
                for (int k = 0; k < ts[i].Count - 1; k++)
                {
                    double a = ts[i][k];
                    double b = ts[i][k + 1];
                    if ((b - a) * source[i].Length <= tol * 0.2) continue;
                    Point2 p1 = source[i].PointAt(a);
                    Point2 p2 = source[i].PointAt(b);
                    result.Add(new Segment2(p1, p2, source[i].SourceId));
                }
            }
            return result;
        }

        private static void AddT(List<double> list, double t, double tol, double length)
        {
            if (t < -1e-8 || t > 1.0 + 1e-8) return;
            double clamped = Math.Max(0.0, Math.Min(1.0, t));
            double eps = Math.Max(1e-8, tol / Math.Max(length, tol));
            foreach (double existing in list)
            {
                if (Math.Abs(existing - clamped) <= eps) return;
            }
            list.Add(clamped);
        }

        private static bool TryIntersect(Segment2 a, Segment2 b, double tol, out double ta, out double tb)
        {
            ta = 0;
            tb = 0;
            Point2 r = a.B - a.A;
            Point2 s = b.B - b.A;
            double denom = r.Cross(s);
            Point2 qp = b.A - a.A;
            if (Math.Abs(denom) < 1e-9)
            {
                return false;
            }
            ta = qp.Cross(s) / denom;
            tb = qp.Cross(r) / denom;
            double eps = tol / Math.Max(Math.Min(a.Length, b.Length), tol);
            return ta >= -eps && ta <= 1.0 + eps && tb >= -eps && tb <= 1.0 + eps;
        }

        private static Graph BuildGraph(List<Segment2> segments, double tol)
        {
            Graph g = new Graph(tol);
            HashSet<string> edges = new HashSet<string>();
            foreach (Segment2 seg in segments)
            {
                int a = g.GetVertex(seg.A);
                int b = g.GetVertex(seg.B);
                if (a == b) continue;
                string key = EdgeKey(a, b);
                if (edges.Contains(key)) continue;
                edges.Add(key);
                g.UndirectedEdges.Add(new IntPair(a, b));
                g.AddDirected(a, b);
                g.AddDirected(b, a);
            }
            foreach (List<DirectedEdge> list in g.Adj.Values)
            {
                list.Sort((x, y) => x.Angle.CompareTo(y.Angle));
            }
            return g;
        }

        private static List<List<int>> EnumerateFaces(Graph g)
        {
            List<List<int>> faces = new List<List<int>>();
            HashSet<string> visited = new HashSet<string>();
            foreach (IntPair edge in g.UndirectedEdges)
            {
                TraceFace(g, edge.A, edge.B, visited, faces);
                TraceFace(g, edge.B, edge.A, visited, faces);
            }
            return faces;
        }

        private static void TraceFace(Graph g, int startA, int startB, HashSet<string> visited, List<List<int>> faces)
        {
            string firstKey = startA.ToString(CultureInfo.InvariantCulture) + ">" + startB.ToString(CultureInfo.InvariantCulture);
            if (visited.Contains(firstKey)) return;

            List<int> cycle = new List<int>();
            int a = startA;
            int b = startB;
            int guard = Math.Max(100, g.UndirectedEdges.Count * 4);
            for (int step = 0; step < guard; step++)
            {
                string key = a.ToString(CultureInfo.InvariantCulture) + ">" + b.ToString(CultureInfo.InvariantCulture);
                if (visited.Contains(key) && !(a == startA && b == startB)) return;
                visited.Add(key);
                cycle.Add(a);

                List<DirectedEdge> outEdges;
                if (!g.Adj.TryGetValue(b, out outEdges) || outEdges.Count == 0) return;

                int backIndex = -1;
                for (int i = 0; i < outEdges.Count; i++)
                {
                    if (outEdges[i].To == a)
                    {
                        backIndex = i;
                        break;
                    }
                }
                if (backIndex < 0) return;

                int nextIndex = (backIndex - 1 + outEdges.Count) % outEdges.Count;
                int c = outEdges[nextIndex].To;
                a = b;
                b = c;

                if (a == startA && b == startB)
                {
                    if (cycle.Count >= 3)
                        faces.Add(cycle);
                    return;
                }
            }
        }

        private static List<int> FindSmallestContainingFace(Graph g, List<List<int>> faces, Point2 point)
        {
            List<int> best = null;
            double bestArea = double.MaxValue;
            foreach (List<int> face in faces)
            {
                double area = Math.Abs(PolygonArea(g, face));
                if (area < 1e-6 || area >= bestArea) continue;
                if (PointInPolygon(g, face, point))
                {
                    best = face;
                    bestArea = area;
                }
            }
            if (best != null) Log("Selected face area=" + bestArea.ToString("0.###", CultureInfo.InvariantCulture));
            return best;
        }

        private static List<List<int>> SelectOuterFaces(Graph g, List<List<int>> faces, bool allLarge)
        {
            Dictionary<string, List<int>> unique = new Dictionary<string, List<int>>();
            Dictionary<string, double> areas = new Dictionary<string, double>();
            Dictionary<string, int> containScores = new Dictionary<string, int>();
            Dictionary<string, double> perimeters = new Dictionary<string, double>();

            foreach (List<int> face in faces)
            {
                if (face == null || face.Count < 3) continue;
                double area = Math.Abs(PolygonArea(g, face));
                if (area < 1e-6) continue;

                string key = FaceVertexSetKey(face);
                double existingArea;
                if (!areas.TryGetValue(key, out existingArea) || area > existingArea)
                {
                    unique[key] = face;
                    areas[key] = area;
                    containScores[key] = CountContainedVertices(g, face);
                    perimeters[key] = PolygonPerimeter(g, face);
                }
            }

            List<List<int>> selected = new List<List<int>>();
            string bestKey = null;
            int bestScore = -1;
            double bestArea = 0.0;
            double bestPerimeter = 0.0;

            foreach (KeyValuePair<string, List<int>> item in unique)
            {
                double area = areas[item.Key];
                int score = containScores[item.Key];
                double perimeter = perimeters[item.Key];
                Log("PCOUTLINE candidate score=" + score.ToString(CultureInfo.InvariantCulture) +
                    ", area=" + area.ToString("0.###", CultureInfo.InvariantCulture) +
                    ", perimeter=" + perimeter.ToString("0.###", CultureInfo.InvariantCulture) +
                    ", vertices=" + item.Value.Count.ToString(CultureInfo.InvariantCulture));

                bool better = false;
                if (score > bestScore)
                    better = true;
                else if (score == bestScore)
                {
                    double areaEps = Math.Max(1e-6, Math.Max(area, bestArea) * 1e-8);
                    if (area > bestArea + areaEps)
                        better = true;
                    else if (Math.Abs(area - bestArea) <= areaEps)
                    {
                        double perimeterEps = Math.Max(1e-6, Math.Max(perimeter, bestPerimeter) * 1e-8);
                        if (perimeter < bestPerimeter - perimeterEps)
                            better = true;
                        else if (Math.Abs(perimeter - bestPerimeter) <= perimeterEps && bestKey != null &&
                                 item.Value.Count > unique[bestKey].Count)
                            better = true;
                    }
                }

                if (better)
                {
                    bestKey = item.Key;
                    bestScore = score;
                    bestArea = area;
                    bestPerimeter = perimeter;
                }
            }

            if (bestKey != null)
                selected.Add(unique[bestKey]);

            Log("PCOUTLINE selected outer face count=" + selected.Count.ToString(CultureInfo.InvariantCulture) +
                ", bestScore=" + bestScore.ToString(CultureInfo.InvariantCulture) +
                ", bestArea=" + bestArea.ToString("0.###", CultureInfo.InvariantCulture) +
                ", bestPerimeter=" + bestPerimeter.ToString("0.###", CultureInfo.InvariantCulture));
            return selected;
        }

        private static List<List<int>> PickOuterFacesByPoint(Editor ed, Graph g, List<List<int>> faces)
        {
            List<List<int>> result = new List<List<int>>();
            PromptPointResult pointRes = ed.GetPoint("\n请在正确的光滑外轮廓线上点一下: ");
            if (pointRes.Status != PromptStatus.OK)
                return result;

            Point2 pick = new Point2(pointRes.Value.X, pointRes.Value.Y);
            List<int> best = null;
            double bestDistance = double.MaxValue;
            double bestArea = 0.0;

            foreach (List<int> face in faces)
            {
                if (face == null || face.Count < 3) continue;
                double area = Math.Abs(PolygonArea(g, face));
                if (area < 1e-6) continue;
                double distance = DistancePointToPolygon(g, face, pick);
                if (distance < bestDistance - 1e-6 ||
                    (Math.Abs(distance - bestDistance) <= 1e-6 && area > bestArea))
                {
                    best = face;
                    bestDistance = distance;
                    bestArea = area;
                }
            }

            if (best != null)
            {
                result.Add(best);
                Log("PCOUTLINE picked face distance=" + bestDistance.ToString("0.###", CultureInfo.InvariantCulture) +
                    ", area=" + bestArea.ToString("0.###", CultureInfo.InvariantCulture));
            }
            return result;
        }

        private static double DistancePointToPolygon(Graph g, List<int> face, Point2 p)
        {
            double best = double.MaxValue;
            for (int i = 0; i < face.Count; i++)
            {
                Point2 a = g.Vertices[face[i]];
                Point2 b = g.Vertices[face[(i + 1) % face.Count]];
                double d = DistancePointToSegment(p, a, b);
                if (d < best) best = d;
            }
            return best;
        }

        private static int CountContainedVertices(Graph g, List<int> face)
        {
            int count = 0;
            for (int i = 0; i < g.Vertices.Count; i++)
            {
                Point2 p = g.Vertices[i];
                if (PointInPolygon(g, face, p) || PointOnPolygon(g, face, p, 1.0))
                    count++;
            }
            return count;
        }

        private static string FaceVertexSetKey(List<int> face)
        {
            List<int> sorted = new List<int>(face);
            sorted.Sort();
            return string.Join(",", sorted.ConvertAll(v => v.ToString(CultureInfo.InvariantCulture)).ToArray());
        }

        private static double PolygonArea(Graph g, List<int> face)
        {
            double area = 0.0;
            for (int i = 0; i < face.Count; i++)
            {
                Point2 a = g.Vertices[face[i]];
                Point2 b = g.Vertices[face[(i + 1) % face.Count]];
                area += a.X * b.Y - b.X * a.Y;
            }
            return area / 2.0;
        }

        private static double PolygonPerimeter(Graph g, List<int> face)
        {
            double perimeter = 0.0;
            for (int i = 0; i < face.Count; i++)
            {
                Point2 a = g.Vertices[face[i]];
                Point2 b = g.Vertices[face[(i + 1) % face.Count]];
                perimeter += a.DistanceTo(b);
            }
            return perimeter;
        }

        private static Point3d GetSelectionCenter(Transaction tr, SelectionSet ss)
        {
            bool hasPoint = false;
            double minX = 0, minY = 0, maxX = 0, maxY = 0;
            foreach (SelectedObject so in ss)
            {
                if (so == null || so.ObjectId.IsNull) continue;
                Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead, false) as Entity;
                if (ent == null) continue;
                try
                {
                    Extents3d ext = ent.GeometricExtents;
                    if (!hasPoint)
                    {
                        minX = ext.MinPoint.X;
                        minY = ext.MinPoint.Y;
                        maxX = ext.MaxPoint.X;
                        maxY = ext.MaxPoint.Y;
                        hasPoint = true;
                    }
                    else
                    {
                        minX = Math.Min(minX, ext.MinPoint.X);
                        minY = Math.Min(minY, ext.MinPoint.Y);
                        maxX = Math.Max(maxX, ext.MaxPoint.X);
                        maxY = Math.Max(maxY, ext.MaxPoint.Y);
                    }
                }
                catch
                {
                }
            }
            if (!hasPoint) return Point3d.Origin;
            return new Point3d((minX + maxX) / 2.0, (minY + maxY) / 2.0, 0.0);
        }

        private static bool PointInPolygon(Graph g, List<int> face, Point2 p)
        {
            bool inside = false;
            for (int i = 0, j = face.Count - 1; i < face.Count; j = i++)
            {
                Point2 pi = g.Vertices[face[i]];
                Point2 pj = g.Vertices[face[j]];
                bool intersect = ((pi.Y > p.Y) != (pj.Y > p.Y)) &&
                    (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / ((pj.Y - pi.Y) == 0 ? 1e-12 : (pj.Y - pi.Y)) + pi.X);
                if (intersect) inside = !inside;
            }
            return inside;
        }

        private static bool PointOnPolygon(Graph g, List<int> face, Point2 p, double tol)
        {
            for (int i = 0; i < face.Count; i++)
            {
                Point2 a = g.Vertices[face[i]];
                Point2 b = g.Vertices[face[(i + 1) % face.Count]];
                if (DistancePointToSegment(p, a, b) <= tol)
                    return true;
            }
            return false;
        }

        private static double DistancePointToSegment(Point2 p, Point2 a, Point2 b)
        {
            Point2 ab = b - a;
            Point2 ap = p - a;
            double len2 = ab.X * ab.X + ab.Y * ab.Y;
            if (len2 <= 1e-12) return p.DistanceTo(a);
            double t = (ap.X * ab.X + ap.Y * ab.Y) / len2;
            t = Math.Max(0.0, Math.Min(1.0, t));
            Point2 projection = new Point2(a.X + ab.X * t, a.Y + ab.Y * t);
            return p.DistanceTo(projection);
        }

        private static Polyline CreatePolyline(Graph g, List<int> face, string layer)
        {
            Polyline pl = new Polyline();
            for (int i = 0; i < face.Count; i++)
            {
                Point2 p = g.Vertices[face[i]];
                pl.AddVertexAt(i, new Point2d(p.X, p.Y), 0, 0, 0);
            }
            pl.Closed = true;
            pl.Layer = layer;
            return pl;
        }

        private static void AddFailureMarker(BlockTableRecord model, Transaction tr, string layer, Point3d center, string text)
        {
            Circle c = new Circle(center, Vector3d.ZAxis, 180.0);
            c.Layer = layer;
            model.AppendEntity(c);
            tr.AddNewlyCreatedDBObject(c, true);

            DBText t = new DBText();
            t.Position = new Point3d(center.X + 220.0, center.Y, center.Z);
            t.Height = 80.0;
            t.TextString = text;
            t.Layer = layer;
            model.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
        }

        private static string EdgeKey(int a, int b)
        {
            return a < b
                ? a.ToString(CultureInfo.InvariantCulture) + ":" + b.ToString(CultureInfo.InvariantCulture)
                : b.ToString(CultureInfo.InvariantCulture) + ":" + a.ToString(CultureInfo.InvariantCulture);
        }

        private static void ResetLog()
        {
            File.WriteAllText(LogPath, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "] log started\r\n");
        }

        private static void Log(string message)
        {
            File.AppendAllText(LogPath, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "] " + message + "\r\n");
        }

        private struct Point2
        {
            public readonly double X;
            public readonly double Y;

            public Point2(double x, double y)
            {
                X = x;
                Y = y;
            }

            public static Point2 operator -(Point2 a, Point2 b)
            {
                return new Point2(a.X - b.X, a.Y - b.Y);
            }

            public static Point2 operator +(Point2 a, Point2 b)
            {
                return new Point2(a.X + b.X, a.Y + b.Y);
            }

            public static Point2 operator *(Point2 a, double t)
            {
                return new Point2(a.X * t, a.Y * t);
            }

            public double Cross(Point2 other)
            {
                return X * other.Y - Y * other.X;
            }

            public double DistanceTo(Point2 other)
            {
                double dx = X - other.X;
                double dy = Y - other.Y;
                return Math.Sqrt(dx * dx + dy * dy);
            }

            public override string ToString()
            {
                return "(" + X.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                    Y.ToString("0.###", CultureInfo.InvariantCulture) + ")";
            }
        }

        private class Segment2
        {
            public readonly Point2 A;
            public readonly Point2 B;
            public readonly ObjectId SourceId;
            public readonly double Length;

            public Segment2(Point2 a, Point2 b, ObjectId sourceId)
            {
                A = a;
                B = b;
                SourceId = sourceId;
                Length = a.DistanceTo(b);
            }

            public Point2 PointAt(double t)
            {
                return A + (B - A) * t;
            }
        }

        private struct IntPair
        {
            public readonly int A;
            public readonly int B;

            public IntPair(int a, int b)
            {
                A = a;
                B = b;
            }
        }

        private class DirectedEdge
        {
            public readonly int To;
            public readonly double Angle;

            public DirectedEdge(int to, double angle)
            {
                To = to;
                Angle = angle;
            }
        }

        private class Graph
        {
            private readonly double _tol;
            public readonly List<Point2> Vertices = new List<Point2>();
            public readonly List<IntPair> UndirectedEdges = new List<IntPair>();
            public readonly Dictionary<int, List<DirectedEdge>> Adj = new Dictionary<int, List<DirectedEdge>>();

            public Graph(double tol)
            {
                _tol = tol;
            }

            public int GetVertex(Point2 p)
            {
                for (int i = 0; i < Vertices.Count; i++)
                {
                    if (Vertices[i].DistanceTo(p) <= _tol)
                        return i;
                }
                Vertices.Add(p);
                return Vertices.Count - 1;
            }

            public void AddDirected(int from, int to)
            {
                List<DirectedEdge> list;
                if (!Adj.TryGetValue(from, out list))
                {
                    list = new List<DirectedEdge>();
                    Adj[from] = list;
                }
                Point2 a = Vertices[from];
                Point2 b = Vertices[to];
                double angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
                list.Add(new DirectedEdge(to, angle));
            }
        }
    }
}
