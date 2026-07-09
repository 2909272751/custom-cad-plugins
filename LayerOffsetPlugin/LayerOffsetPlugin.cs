using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace LayerOffsetPlugin
{
    public class LayerOffsetCommand : IExtensionApplication
    {
        private const string DirectionIn = "N";
        private const string DirectionOut = "W";

        public void Initialize()
        {
        }

        public void Terminate()
        {
        }

        [CommandMethod("LOFFSET")]
        public void OffsetLayerEntities()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;
            List<ObjectId> highlightedIds = new List<ObjectId>();

            try
            {
                PromptEntityResult pickResult = PromptForLayerSource(ed);
                if (pickResult.Status != PromptStatus.OK)
                {
                    return;
                }

                string layerName;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Entity source = tr.GetObject(pickResult.ObjectId, OpenMode.ForRead) as Entity;
                    if (source == null)
                    {
                        ed.WriteMessage("\n\u9009\u62e9\u7684\u5bf9\u8c61\u4e0d\u662f\u6709\u6548\u5b9e\u4f53\u3002");
                        return;
                    }

                    layerName = source.Layer;
                    highlightedIds = CollectLayerEntityIds(db, tr, layerName);
                    HighlightEntities(tr, highlightedIds, true);
                    tr.Commit();
                }

                ed.WriteMessage(string.Format("\n\u5df2\u9009\u62e9\u56fe\u5c42\uff1a{0}\uff0c\u5f53\u524d\u7a7a\u95f4\u5171\u627e\u5230 {1} \u4e2a\u5bf9\u8c61\u3002", layerName, highlightedIds.Count));

                PromptResult confirmLayerResult = PromptForLayerConfirmation(ed);
                if (confirmLayerResult.Status != PromptStatus.OK ||
                    string.Equals(confirmLayerResult.StringResult, "N", StringComparison.OrdinalIgnoreCase))
                {
                    ed.WriteMessage("\n\u5df2\u53d6\u6d88\uff0c\u8bf7\u91cd\u65b0\u6267\u884c LOFFSET \u9009\u62e9\u56fe\u5c42\u3002");
                    return;
                }

                List<ObjectId> offsetIds = PromptForOffsetSelection(ed, highlightedIds);
                if (offsetIds == null)
                {
                    return;
                }

                if (offsetIds.Count == 0)
                {
                    ed.WriteMessage("\n\u6846\u9009\u8303\u56f4\u5185\u6ca1\u6709\u627e\u5230\u6240\u9009\u56fe\u5c42\u7684\u5bf9\u8c61\u3002");
                    return;
                }

                ed.WriteMessage(string.Format("\n\u5df2\u9009\u4e2d {0} \u4e2a\u56fe\u5c42 {1} \u4e0a\u7684\u5bf9\u8c61\u7528\u4e8e offset\u3002", offsetIds.Count, layerName));

                PromptDoubleResult distanceResult = PromptForDistance(ed);
                if (distanceResult.Status != PromptStatus.OK)
                {
                    return;
                }

                PromptResult directionResult = PromptForDirection(ed);
                if (directionResult.Status != PromptStatus.OK)
                {
                    return;
                }

                PromptResult deleteResult = PromptForDeleteOriginal(ed);
                if (deleteResult.Status != PromptStatus.OK)
                {
                    return;
                }

                OffsetOptions options = new OffsetOptions
                {
                    Distance = distanceResult.Value,
                    OffsetOutward = string.Equals(directionResult.StringResult, DirectionOut, StringComparison.OrdinalIgnoreCase),
                    DeleteOriginal = string.Equals(deleteResult.StringResult, "Y", StringComparison.OrdinalIgnoreCase)
                };

                OffsetStats stats;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    stats = OffsetEntities(tr, currentSpace, offsetIds, options);
                    tr.Commit();
                }

                ed.WriteMessage(
                    string.Format(
                        "\n\u5b8c\u6210\uff1a\u6210\u529f offset {0} \u4e2a\uff0c\u5220\u9664\u539f\u56fe {1} \u4e2a\uff0c\u8df3\u8fc7 {2} \u4e2a\uff0c\u5931\u8d25 {3} \u4e2a\u3002",
                        stats.SuccessCount,
                        stats.DeletedOriginalCount,
                        stats.SkippedCount,
                        stats.FailedCount));
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format("\nLOFFSET \u6267\u884c\u5931\u8d25\uff1a{0}", ex.Message));
            }
            finally
            {
                UnhighlightEntities(db, highlightedIds);
            }
        }

        private static PromptEntityResult PromptForLayerSource(Editor ed)
        {
            PromptEntityOptions options = new PromptEntityOptions("\n\u8bf7\u9009\u62e9\u9700\u8981 offset \u7684\u56fe\u5c42\u4e0a\u7684\u4efb\u610f\u5bf9\u8c61: ");
            options.AllowNone = false;
            return ed.GetEntity(options);
        }

        private static PromptResult PromptForLayerConfirmation(Editor ed)
        {
            PromptKeywordOptions options = new PromptKeywordOptions("\n\u662f\u5426\u786e\u8ba4\u4f7f\u7528\u5f53\u524d\u9ad8\u4eae\u7684\u56fe\u5c42 [\u662f(Y)/\u5426(N)] <Y>: ");
            options.Keywords.Add("Y");
            options.Keywords.Add("N");
            options.Keywords.Default = "Y";
            options.AllowNone = true;
            return ed.GetKeywords(options);
        }

        private static List<ObjectId> PromptForOffsetSelection(Editor ed, IEnumerable<ObjectId> layerEntityIds)
        {
            PromptSelectionOptions options = new PromptSelectionOptions();
            options.MessageForAdding = "\n\u8bf7\u6846\u9009/\u4ea4\u53c9\u6846\u9009\u9700\u8981 offset \u7684\u533a\u57df\u6216\u5bf9\u8c61: ";
            options.AllowDuplicates = false;

            PromptSelectionResult result = ed.GetSelection(options);
            if (result.Status != PromptStatus.OK)
            {
                return null;
            }

            HashSet<ObjectId> layerIdSet = new HashSet<ObjectId>(layerEntityIds);
            List<ObjectId> offsetIds = new List<ObjectId>();

            foreach (SelectedObject selected in result.Value)
            {
                if (selected == null || selected.ObjectId.IsNull)
                {
                    continue;
                }

                if (layerIdSet.Contains(selected.ObjectId))
                {
                    offsetIds.Add(selected.ObjectId);
                }
            }

            return offsetIds;
        }

        private static PromptDoubleResult PromptForDistance(Editor ed)
        {
            PromptDoubleOptions options = new PromptDoubleOptions("\n\u8bf7\u8f93\u5165 offset \u8ddd\u79bb: ");
            options.AllowNegative = false;
            options.AllowZero = false;
            options.AllowNone = false;
            return ed.GetDouble(options);
        }

        private static PromptResult PromptForDirection(Editor ed)
        {
            PromptKeywordOptions options = new PromptKeywordOptions("\n\u8bf7\u8f93\u5165\u65b9\u5411 [\u5185(N)/\u5916(W)] <W>: ");
            options.Keywords.Add(DirectionIn);
            options.Keywords.Add(DirectionOut);
            options.Keywords.Default = DirectionOut;
            options.AllowNone = true;
            return ed.GetKeywords(options);
        }

        private static PromptResult PromptForDeleteOriginal(Editor ed)
        {
            PromptKeywordOptions options = new PromptKeywordOptions("\n\u662f\u5426\u5220\u9664\u539f\u56fe\u5f62 [\u662f(Y)/\u5426(N)] <N>: ");
            options.Keywords.Add("Y");
            options.Keywords.Add("N");
            options.Keywords.Default = "N";
            options.AllowNone = true;
            return ed.GetKeywords(options);
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
                // AutoCAD may already have disposed transient highlight state while ending a command.
            }
        }

        private static OffsetStats OffsetEntities(
            Transaction tr,
            BlockTableRecord currentSpace,
            IEnumerable<ObjectId> ids,
            OffsetOptions options)
        {
            OffsetStats stats = new OffsetStats();

            foreach (ObjectId id in ids)
            {
                Entity entity = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                Curve curve = entity as Curve;
                if (entity == null || curve == null || entity.IsErased)
                {
                    stats.SkippedCount++;
                    continue;
                }

                try
                {
                    DBObjectCollection offsetCurves = CreateOffsetCurves(curve, options);
                    if (offsetCurves == null || offsetCurves.Count == 0)
                    {
                        stats.FailedCount++;
                        continue;
                    }

                    foreach (DBObject obj in offsetCurves)
                    {
                        Entity offsetEntity = obj as Entity;
                        if (offsetEntity == null)
                        {
                            obj.Dispose();
                            continue;
                        }

                        offsetEntity.SetPropertiesFrom(entity);
                        offsetEntity.Layer = entity.Layer;
                        currentSpace.AppendEntity(offsetEntity);
                        tr.AddNewlyCreatedDBObject(offsetEntity, true);
                    }

                    stats.SuccessCount++;

                    if (options.DeleteOriginal)
                    {
                        Entity writableOriginal = (Entity)tr.GetObject(id, OpenMode.ForWrite, false);
                        writableOriginal.Erase();
                        stats.DeletedOriginalCount++;
                    }
                }
                catch
                {
                    stats.FailedCount++;
                }
            }

            return stats;
        }

        private static DBObjectCollection CreateOffsetCurves(Curve curve, OffsetOptions options)
        {
            if (IsClosedAreaCurve(curve))
            {
                DBObjectCollection positive = TryGetOffsetCurves(curve, options.Distance);
                DBObjectCollection negative = TryGetOffsetCurves(curve, -options.Distance);
                DBObjectCollection selected = SelectClosedOffsetByArea(curve, positive, negative, options.OffsetOutward);

                if (ReferenceEquals(selected, positive))
                {
                    DisposeCollection(negative);
                }
                else
                {
                    DisposeCollection(positive);
                }

                return selected;
            }

            double signedDistance = options.OffsetOutward ? options.Distance : -options.Distance;
            return curve.GetOffsetCurves(signedDistance);
        }

        private static DBObjectCollection TryGetOffsetCurves(Curve curve, double distance)
        {
            try
            {
                return curve.GetOffsetCurves(distance);
            }
            catch
            {
                return null;
            }
        }

        private static DBObjectCollection SelectClosedOffsetByArea(
            Curve original,
            DBObjectCollection positive,
            DBObjectCollection negative,
            bool offsetOutward)
        {
            double? originalArea = GetArea(original);
            double? positiveArea = GetCollectionArea(positive);
            double? negativeArea = GetCollectionArea(negative);

            if (positiveArea.HasValue && negativeArea.HasValue)
            {
                bool positiveIsOutward = positiveArea.Value >= negativeArea.Value;
                return positiveIsOutward == offsetOutward ? positive : negative;
            }

            if (originalArea.HasValue && positiveArea.HasValue)
            {
                bool positiveIsOutward = positiveArea.Value >= originalArea.Value;
                if (positiveIsOutward == offsetOutward)
                {
                    return positive;
                }
            }

            if (originalArea.HasValue && negativeArea.HasValue)
            {
                bool negativeIsOutward = negativeArea.Value >= originalArea.Value;
                if (negativeIsOutward == offsetOutward)
                {
                    return negative;
                }
            }

            return offsetOutward ? positive ?? negative : negative ?? positive;
        }

        private static bool IsClosedAreaCurve(Curve curve)
        {
            try
            {
                if (!curve.Closed)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            double? area = GetArea(curve);
            return area.HasValue && area.Value > Tolerance.Global.EqualVector;
        }

        private static double? GetCollectionArea(DBObjectCollection objects)
        {
            if (objects == null || objects.Count == 0)
            {
                return null;
            }

            double area = 0.0;
            bool foundArea = false;

            foreach (DBObject obj in objects)
            {
                Curve curve = obj as Curve;
                double? curveArea = GetArea(curve);
                if (curveArea.HasValue)
                {
                    area += curveArea.Value;
                    foundArea = true;
                }
            }

            return foundArea ? area : (double?)null;
        }

        private static double? GetArea(Curve curve)
        {
            if (curve == null)
            {
                return null;
            }

            try
            {
                Polyline polyline = curve as Polyline;
                if (polyline != null && polyline.Closed)
                {
                    return Math.Abs(polyline.Area);
                }

                Circle circle = curve as Circle;
                if (circle != null)
                {
                    return Math.PI * circle.Radius * circle.Radius;
                }

                Ellipse ellipse = curve as Ellipse;
                if (ellipse != null && ellipse.Closed)
                {
                    double minorRadius = ellipse.MajorRadius * ellipse.RadiusRatio;
                    return Math.PI * ellipse.MajorRadius * minorRadius;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static void DisposeCollection(DBObjectCollection objects)
        {
            if (objects == null)
            {
                return;
            }

            foreach (DBObject obj in objects)
            {
                obj.Dispose();
            }
        }

        private class OffsetOptions
        {
            public double Distance { get; set; }

            public bool OffsetOutward { get; set; }

            public bool DeleteOriginal { get; set; }
        }

        private class OffsetStats
        {
            public int SuccessCount { get; set; }

            public int SkippedCount { get; set; }

            public int FailedCount { get; set; }

            public int DeletedOriginalCount { get; set; }
        }
    }
}
