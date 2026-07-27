using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(LbcpPlugin.LbcpCommands))]

namespace LbcpPlugin
{
    public class LbcpCommands
    {
        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "LBCP.log");
        private static readonly Regex BeamNumberRegex = new Regex(@"^\s*([A-Za-z]+)\s*(\d+)", RegexOptions.Compiled);

        [CommandMethod("LBCP")]
        public void SortBeamTable()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            Log("========== LBCP start " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " ==========");

            try
            {
                PromptSelectionResult selectionResult = ed.GetSelection(new PromptSelectionOptions
                {
                    MessageForAdding = "\n请框选需要整理的梁表范围: "
                });

                if (selectionResult.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n已取消。");
                    Log("User canceled selection.");
                    return;
                }

                PromptEntityOptions entityOptions = new PromptEntityOptions("\n请选择编号列中的一个梁编号文字，用来确定编号列: ");
                entityOptions.SetRejectMessage("\n请选择单行文字或多行文字。");
                entityOptions.AddAllowedClass(typeof(DBText), false);
                entityOptions.AddAllowedClass(typeof(MText), false);
                PromptEntityResult columnResult = ed.GetEntity(entityOptions);
                if (columnResult.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n已取消。");
                    Log("User canceled sample text.");
                    return;
                }

                PromptStringOptions orderOptions = new PromptStringOptions("\n请输入分类顺序，如 L,LL,KL,XL <L,LL,KL,XL>: ");
                orderOptions.AllowSpaces = false;
                orderOptions.DefaultValue = "L,LL,KL,XL";
                orderOptions.UseDefaultValue = true;
                PromptResult orderResult = ed.GetString(orderOptions);
                if (orderResult.Status != PromptStatus.OK && orderResult.Status != PromptStatus.None)
                {
                    ed.WriteMessage("\n已取消。");
                    Log("User canceled category order.");
                    return;
                }

                string orderText = string.IsNullOrWhiteSpace(orderResult.StringResult) ? "L,LL,KL,XL" : orderResult.StringResult;
                Dictionary<string, int> orderMap = ParseCategoryOrder(orderText);
                if (orderMap.Count == 0)
                {
                    ed.WriteMessage("\n分类顺序为空，已取消。");
                    Log("Empty category order.");
                    return;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    TextItem sample = ReadTextItem(tr, columnResult.ObjectId);
                    if (sample == null)
                    {
                        ed.WriteMessage("\n无法读取编号列文字，已取消。");
                        Log("Sample text cannot be read.");
                        return;
                    }

                    List<TextItem> items = CollectTextItems(tr, selectionResult.Value);
                    if (items.Count == 0)
                    {
                        ed.WriteMessage("\n框选范围内没有找到文字对象。");
                        Log("No text in selection.");
                        return;
                    }

                    double medianHeight = Median(items.Select(i => i.Height).Where(h => h > 0.0).ToList());
                    if (medianHeight <= 0.0) medianHeight = 1.0;
                    double rowTolerance = medianHeight * 1.35;

                    List<RowInfo> rows = BuildRows(items, rowTolerance);
                    List<RowInfo> sortableRows = DetectSortableRows(rows, sample.Center.X, medianHeight, orderMap);

                    int skippedRows = rows.Count - sortableRows.Count;
                    Log("Text count: " + items.Count);
                    Log("Row count: " + rows.Count);
                    Log("Sortable row count: " + sortableRows.Count);
                    Log("Skipped row count: " + skippedRows);
                    foreach (RowInfo row in sortableRows)
                    {
                        Log("Sortable row: " + row.NumberText + ", prefix=" + row.Prefix + ", number=" + row.Number.ToString(CultureInfo.InvariantCulture) + ", y=" + row.CenterY.ToString("0.###", CultureInfo.InvariantCulture));
                    }

                    if (sortableRows.Count < 2)
                    {
                        ed.WriteMessage("\n可排序的梁编号行少于 2 行，不需要重排。已检查 {0} 行，跳过 {1} 行。", rows.Count, skippedRows);
                        tr.Commit();
                        return;
                    }

                    string preview = BuildPreview(sortableRows, orderMap);
                    ed.WriteMessage("\n识别到 {0} 行可排序梁编号，跳过 {1} 行表头或非梁编号。", sortableRows.Count, skippedRows);
                    ed.WriteMessage("\n排序预览: {0}", preview);

                    if (!AskYesNo(ed, "\n是否确认按以上顺序重排 [是(Y)/否(N)] <Y>: ", true))
                    {
                        ed.WriteMessage("\n已取消，没有修改图形。");
                        Log("User rejected sort.");
                        tr.Commit();
                        return;
                    }

                    List<RowInfo> targetRows = sortableRows.OrderByDescending(r => r.CenterY).ToList();
                    List<RowInfo> sortedRows = sortableRows
                        .OrderBy(r => GetOrderRank(orderMap, r.Prefix))
                        .ThenBy(r => r.Number)
                        .ThenBy(r => r.NumberText, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    for (int i = 0; i < sortedRows.Count; i++)
                    {
                        RowInfo source = sortedRows[i];
                        RowInfo target = targetRows[i];
                        double deltaY = target.CenterY - source.CenterY;
                        if (Math.Abs(deltaY) < 1e-8) continue;

                        Log("Move row " + source.NumberText + " from y=" + source.CenterY.ToString("0.###", CultureInfo.InvariantCulture) + " to y=" + target.CenterY.ToString("0.###", CultureInfo.InvariantCulture));
                        foreach (TextItem item in source.Items)
                        {
                            Entity ent = tr.GetObject(item.Id, OpenMode.ForWrite, false) as Entity;
                            if (ent == null) continue;
                            ent.TransformBy(Matrix3d.Displacement(new Vector3d(0.0, deltaY, 0.0)));
                        }
                    }

                    tr.Commit();
                    ed.WriteMessage("\n完成：已重排 {0} 行，跳过 {1} 行表头或非梁编号。", sortableRows.Count, skippedRows);
                    Log("Completed.");
                }
            }
            catch (System.Exception ex)
            {
                Log("ERROR: " + ex);
                ed.WriteMessage("\nLBCP 出错：{0}\n可输入 LBCPLOG 查看日志。", ex.Message);
            }
        }

        [CommandMethod("LBCPLOG")]
        public void ShowLogPath()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            doc.Editor.WriteMessage("\nLBCP 日志路径: {0}", LogPath);
        }

        private static List<TextItem> CollectTextItems(Transaction tr, SelectionSet set)
        {
            List<TextItem> result = new List<TextItem>();
            foreach (SelectedObject selected in set)
            {
                if (selected == null || selected.ObjectId.IsNull) continue;
                TextItem item = ReadTextItem(tr, selected.ObjectId);
                if (item != null && !string.IsNullOrWhiteSpace(item.Text))
                {
                    result.Add(item);
                }
            }
            return result;
        }

        private static TextItem ReadTextItem(Transaction tr, ObjectId id)
        {
            Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
            if (ent == null) return null;

            string text = null;
            Point3d fallbackPoint = Point3d.Origin;
            double fallbackHeight = 0.0;

            DBText dbText = ent as DBText;
            if (dbText != null)
            {
                text = dbText.TextString;
                fallbackPoint = dbText.Position;
                fallbackHeight = dbText.Height;
            }

            MText mText = ent as MText;
            if (mText != null)
            {
                text = StripMText(mText.Contents);
                fallbackPoint = mText.Location;
                fallbackHeight = mText.TextHeight;
            }

            if (text == null) return null;

            Point3d center = fallbackPoint;
            double width = 0.0;
            double height = fallbackHeight;
            try
            {
                Extents3d ext = ent.GeometricExtents;
                center = new Point3d(
                    (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                    (ext.MinPoint.Z + ext.MaxPoint.Z) * 0.5);
                width = Math.Abs(ext.MaxPoint.X - ext.MinPoint.X);
                height = Math.Abs(ext.MaxPoint.Y - ext.MinPoint.Y);
            }
            catch
            {
                width = 0.0;
                if (height <= 0.0) height = 1.0;
            }

            return new TextItem
            {
                Id = id,
                Text = NormalizeText(text),
                Center = center,
                Height = height <= 0.0 ? 1.0 : height,
                Width = width
            };
        }

        private static List<RowInfo> BuildRows(List<TextItem> items, double tolerance)
        {
            List<RowInfo> rows = new List<RowInfo>();
            foreach (TextItem item in items.OrderByDescending(i => i.Center.Y))
            {
                RowInfo row = rows.FirstOrDefault(r => Math.Abs(r.CenterY - item.Center.Y) <= tolerance);
                if (row == null)
                {
                    row = new RowInfo();
                    rows.Add(row);
                }

                row.Items.Add(item);
                row.CenterY = row.Items.Average(i => i.Center.Y);
            }

            return rows.OrderByDescending(r => r.CenterY).ToList();
        }

        private static List<RowInfo> DetectSortableRows(List<RowInfo> rows, double columnX, double medianHeight, Dictionary<string, int> orderMap)
        {
            List<RowInfo> result = new List<RowInfo>();
            double generousColumnDistance = medianHeight * 12.0;

            foreach (RowInfo row in rows)
            {
                List<TextItem> ordered = row.Items
                    .OrderBy(i => Math.Abs(i.Center.X - columnX))
                    .ToList();

                TextItem numberItem = null;
                BeamNumber beamNumber = null;
                foreach (TextItem item in ordered)
                {
                    double distance = Math.Abs(item.Center.X - columnX);
                    if (numberItem != null && distance > generousColumnDistance) break;

                    BeamNumber parsed = ParseBeamNumber(item.Text);
                    if (parsed == null) continue;

                    numberItem = item;
                    beamNumber = parsed;
                    break;
                }

                if (numberItem == null || beamNumber == null)
                {
                    Log("Skipped row y=" + row.CenterY.ToString("0.###", CultureInfo.InvariantCulture) + ", nearest='" + (ordered.Count > 0 ? ordered[0].Text : "") + "'");
                    continue;
                }

                row.NumberItem = numberItem;
                row.NumberText = numberItem.Text;
                row.Prefix = beamNumber.Prefix;
                row.Number = beamNumber.Number;
                result.Add(row);
            }

            return result;
        }

        private static BeamNumber ParseBeamNumber(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string compact = Regex.Replace(text, @"\s+", "").ToUpperInvariant();
            Match match = BeamNumberRegex.Match(compact);
            if (!match.Success) return null;

            int number;
            if (!int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                return null;
            }

            return new BeamNumber
            {
                Prefix = match.Groups[1].Value.ToUpperInvariant(),
                Number = number
            };
        }

        private static Dictionary<string, int> ParseCategoryOrder(string orderText)
        {
            Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string[] parts = orderText.Split(new[] { ',', '，', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string key = parts[i].Trim().ToUpperInvariant();
                if (key.Length == 0 || map.ContainsKey(key)) continue;
                map.Add(key, map.Count);
            }
            return map;
        }

        private static int GetOrderRank(Dictionary<string, int> orderMap, string prefix)
        {
            int rank;
            if (orderMap.TryGetValue(prefix, out rank)) return rank;
            return orderMap.Count + 100;
        }

        private static string BuildPreview(List<RowInfo> rows, Dictionary<string, int> orderMap)
        {
            List<string> names = rows
                .OrderBy(r => GetOrderRank(orderMap, r.Prefix))
                .ThenBy(r => r.Number)
                .ThenBy(r => r.NumberText, StringComparer.OrdinalIgnoreCase)
                .Select(r => r.NumberText)
                .ToList();

            const int max = 18;
            if (names.Count > max)
            {
                return string.Join(", ", names.Take(max).ToArray()) + " ...";
            }
            return string.Join(", ", names.ToArray());
        }

        private static bool AskYesNo(Editor ed, string message, bool defaultYes)
        {
            PromptKeywordOptions options = new PromptKeywordOptions(message);
            options.Keywords.Add("Y");
            options.Keywords.Add("N");
            options.Keywords.Default = defaultYes ? "Y" : "N";
            options.AllowNone = true;

            PromptResult result = ed.GetKeywords(options);
            if (result.Status == PromptStatus.None) return defaultYes;
            if (result.Status != PromptStatus.OK) return false;
            return string.Equals(result.StringResult, "Y", StringComparison.OrdinalIgnoreCase);
        }

        private static double Median(List<double> values)
        {
            if (values == null || values.Count == 0) return 0.0;
            values.Sort();
            int mid = values.Count / 2;
            if (values.Count % 2 == 1) return values[mid];
            return (values[mid - 1] + values[mid]) * 0.5;
        }

        private static string NormalizeText(string text)
        {
            if (text == null) return "";
            return text.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static string StripMText(string text)
        {
            if (text == null) return "";
            string value = text.Replace("\\P", " ");
            value = Regex.Replace(value, @"\\[A-Za-z]+[^;]*;", "");
            value = value.Replace("{", "").Replace("}", "");
            return value;
        }

        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogPath, message + Environment.NewLine);
            }
            catch
            {
            }
        }

        private class TextItem
        {
            public ObjectId Id;
            public string Text;
            public Point3d Center;
            public double Height;
            public double Width;
        }

        private class RowInfo
        {
            public readonly List<TextItem> Items = new List<TextItem>();
            public double CenterY;
            public TextItem NumberItem;
            public string NumberText;
            public string Prefix;
            public int Number;
        }

        private class BeamNumber
        {
            public string Prefix;
            public int Number;
        }
    }
}
