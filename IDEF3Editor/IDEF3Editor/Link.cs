using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace IDEF3Editor
{
    // Связь между двумя блоками диаграммы IDEF3.
    // Три типа по стандарту:
    //   precedence  — предшествования (сплошная линия)
    //   relational  — реляционная     (пунктирная линия)
    //   object-flow — поток объектов  (линия из точек)
    public class Link
    {
        public string Id       { get; set; } = Guid.NewGuid().ToString();
        public string SourceId { get; set; }
        public string TargetId { get; set; }

        private string _type = "precedence";

        // Тип связи. При изменении немедленно обновляет визуальный стиль линии.
        public string Type
        {
            get => _type;
            set { _type = value; ApplyTypeStyle(); }
        }

        public string      Label       { get; set; } = "";
        public TextBlock   LabelElement { get; set; }

        public Path        PathElement { get; set; }
        public Path        ArrowHead   { get; set; }
        public List<Point> UserPoints  { get; set; } = new List<Point>();

        private static readonly Color DefaultColor  = Color.FromRgb(44,  62,  80);
        private static readonly Color SelectedColor = Color.FromRgb(232, 101, 10);

        // ═════════════════════════════════════════════════════════════

        public void CreateVisual()
        {
            PathElement = new Path
            {
                Stroke          = new SolidColorBrush(DefaultColor),
                StrokeThickness = 2,
                StrokeLineJoin  = PenLineJoin.Round,
                Fill            = Brushes.Transparent,
                Tag             = this
            };
            ArrowHead = new Path
            {
                Fill            = new SolidColorBrush(DefaultColor),
                Stroke          = new SolidColorBrush(DefaultColor),
                StrokeThickness = 1,
                Tag             = this
            };
            LabelElement = new TextBlock
            {
                Text            = Label ?? "",
                FontSize        = 10,
                Foreground      = new SolidColorBrush(DefaultColor),
                FontFamily      = new System.Windows.Media.FontFamily("Arial"),
                Background      = new SolidColorBrush(Color.FromArgb(200, 250, 250, 250)),
                Padding         = new Thickness(3, 1, 3, 1),
                Visibility      = string.IsNullOrEmpty(Label) ? Visibility.Collapsed : Visibility.Visible,
                Tag             = this
            };
            ApplyTypeStyle();
        }

        // Применяет стиль штриховки по текущему типу связи.
        public void ApplyTypeStyle()
        {
            if (PathElement == null) return;
            switch (_type)
            {
                case "relational":
                    PathElement.StrokeDashArray = new DoubleCollection { 6, 3 };
                    PathElement.StrokeDashCap   = PenLineCap.Round;
                    break;
                case "object-flow":
                    PathElement.StrokeDashArray = new DoubleCollection { 1.5, 3 };
                    PathElement.StrokeDashCap   = PenLineCap.Round;
                    break;
                default: // предшествования — сплошная
                    PathElement.StrokeDashArray = null;
                    break;
            }
        }

        public void Highlight(bool isSelected)
        {
            var brush = isSelected
                ? (Brush)new SolidColorBrush(SelectedColor)
                : (Brush)new SolidColorBrush(DefaultColor);

            if (PathElement != null)
            {
                PathElement.Stroke          = brush;
                PathElement.StrokeThickness = isSelected ? 2.5 : 2;
                PathElement.Effect          = isSelected
                    ? new System.Windows.Media.Effects.DropShadowEffect
                      { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.4, Color = SelectedColor }
                    : null;
            }
            if (ArrowHead != null) { ArrowHead.Fill = brush; ArrowHead.Stroke = brush; }
            if (LabelElement != null) LabelElement.Foreground = brush;
        }

        // Обновляет текст метки и показывает/скрывает элемент.
        public void RefreshLabel()
        {
            if (LabelElement == null) return;
            LabelElement.Text       = Label ?? "";
            LabelElement.Visibility = string.IsNullOrEmpty(Label)
                ? Visibility.Collapsed : Visibility.Visible;
        }

        // ═════════════════════════════════════════════════════════════
        // МАРШРУТИЗАЦИЯ
        // ═════════════════════════════════════════════════════════════

        // Пересчитывает маршрут и обновляет геометрию.
        // obstacles — полный список блоков диаграммы для обхода препятствий.
        public void UpdatePosition(Node source, Node target,
                                   List<Node> obstacles = null,
                                   List<Link> allLinks  = null)
        {
            if (PathElement == null || ArrowHead == null || source == null || target == null) return;
            List<Point> route = UserPoints.Count > 0
                ? BuildRouteWithWaypoints(source, target)
                : BuildRoute(source, target, obstacles, allLinks);
            DrawPolyline(route);
        }

        // Строит ортогональный маршрут между двумя блоками с обходом препятствий
        // и разделением параллельных связей (lane separation).
        ///
        // Детерминированный трёхшаговый алгоритм (без итераций, без каскадных петель):
        //   Шаг 1 — Найти midY/midX так, чтобы СРЕДНЯЯ ПЕРЕМЫЧКА не пересекала блоки.
        //   Шаг 2 — Если ВЫХОДНОЙ сегмент (источник→перемычка) пересекает блок,
        //            добавить U-образный обход.
        //   Шаг 3 — Аналогично для ВХОДНОГО сегмента (перемычка→цель).
        private List<Point> BuildRoute(Node source, Node target,
                                       List<Node> obstacles, List<Link> allLinks)
        {
            const double pad     = 20;
            const double spacing =  8; // расстояние между параллельными портами

            double srcCx = source.X + source.Width  / 2;
            double srcCy = source.Y + source.Height / 2;
            double tgtCx = target.X + target.Width  / 2;
            double tgtCy = target.Y + target.Height / 2;
            double dx = tgtCx - srcCx, dy = tgtCy - srcCy;

            bool vertical = Math.Abs(dy) >= Math.Abs(dx);

            // Смещения портов для разделения параллельных связей
            // obstacles — полный список узлов (не фильтрованный), используется для поиска
            double exitOff  = GetPortOffset(Id, source.Id, isExit: true,
                                            vertical, obstacles, allLinks, spacing);
            double entryOff = GetPortOffset(Id, target.Id, isExit: false,
                                            vertical, obstacles, allLinks, spacing);

            Point exit, entry;

            if (vertical)
            {
                bool goDown = dy >= 0;
                // exitOff/entryOff смещают порт по X вдоль горизонтального края
                exit  = goDown
                    ? new Point(Clamp(srcCx + exitOff,  source.X, source.X + source.Width), source.Y + source.Height)
                    : new Point(Clamp(srcCx + exitOff,  source.X, source.X + source.Width), source.Y);
                entry = goDown
                    ? new Point(Clamp(tgtCx + entryOff, target.X, target.X + target.Width), target.Y)
                    : new Point(Clamp(tgtCx + entryOff, target.X, target.X + target.Width), target.Y + target.Height);
            }
            else
            {
                bool goRight = dx >= 0;
                // exitOff/entryOff смещают порт по Y вдоль вертикального края
                exit  = goRight
                    ? new Point(source.X + source.Width,  Clamp(srcCy + exitOff,  source.Y, source.Y + source.Height))
                    : new Point(source.X,                 Clamp(srcCy + exitOff,  source.Y, source.Y + source.Height));
                entry = goRight
                    ? new Point(target.X,                 Clamp(tgtCy + entryOff, target.Y, target.Y + target.Height))
                    : new Point(target.X + target.Width,  Clamp(tgtCy + entryOff, target.Y, target.Y + target.Height));
            }

            var obs = obstacles?.Where(n => n != source && n != target).ToList()
                      ?? new List<Node>();

            if (vertical)
                return BuildVerticalRoute(exit, entry, obs, pad);
            else
                return BuildHorizontalRoute(exit, entry, obs, pad);
        }

        // ─── Разделение параллельных портов (lane separation) ────────

        // Вычисляет смещение порта данной связи среди всех параллельных связей,
        // выходящих из того же блока (или входящих в тот же блок) в том же направлении.
        ///
        // Порты упорядочиваются по позиции «другого конца» связи:
        // для вертикальных маршрутов — по X цели (левые цели → левые порты),
        // для горизонтальных — по Y цели (верхние цели → верхние порты).
        // Это гарантирует, что линии расходятся сразу от блока и не пересекаются.
        private static double GetPortOffset(string thisId, string nodeId,
                                             bool isExit, bool vertical,
                                             List<Node> allNodes, List<Link> allLinks,
                                             double spacing)
        {
            if (allLinks == null || allLinks.Count <= 1) return 0;

            var group = allLinks
                .Where(l =>
                {
                    bool sameNode = isExit ? l.SourceId == nodeId : l.TargetId == nodeId;
                    if (!sameNode) return false;

                    var lSrc = allNodes?.Find(n => n.Id == l.SourceId);
                    var lTgt = allNodes?.Find(n => n.Id == l.TargetId);
                    if (lSrc == null || lTgt == null) return true;

                    double ldx = (lTgt.X + lTgt.Width  / 2) - (lSrc.X + lSrc.Width  / 2);
                    double ldy = (lTgt.Y + lTgt.Height / 2) - (lSrc.Y + lSrc.Height / 2);
                    return (Math.Abs(ldy) >= Math.Abs(ldx)) == vertical;
                })
                .OrderBy(l =>
                {
                    // Сортировка по Y-расстоянию для вертикальных маршрутов:
                    // ближайшая цель получает idx=0 (центральный порт, offset=0),
                    // дальние цели — большие смещения. Это устраняет наложение линий,
                    // когда несколько блоков стоят в одном столбце (одинаковый X).
                    string otherId = isExit ? l.TargetId : l.SourceId;
                    var other = allNodes?.Find(n => n.Id == otherId);
                    if (other == null) return 0.0;
                    // Вертикальные маршруты → сортируем по Y цели (ближайшая = idx=0 = центр)
                    // Горизонтальные маршруты → по X цели
                    return vertical
                        ? other.Y + other.Height / 2.0
                        : other.X + other.Width  / 2.0;
                })
                .ToList();

            int count = group.Count;
            if (count <= 1) return 0;

            int idx = group.FindIndex(l => l.Id == thisId);
            if (idx < 0) return 0;

            // idx=0 → ближайший блок → offset=0 (центр, прямая линия)
            // idx=1,2,... → дальние блоки → смещение вправо (они всё равно делают объезд)
            return idx * spacing;
        }

        private static double Clamp(double v, double lo, double hi)
            => v < lo ? lo : v > hi ? hi : v;

        // ─── Построение маршрута по направлению ──────────────────────

        private static List<Point> BuildVerticalRoute(Point exit, Point entry,
                                                       List<Node> obs, double pad)
        {
            // Шаг 1: найти midY — горизонтальный сегмент без пересечений
            double midY = (exit.Y + entry.Y) / 2;
            midY = AdjustMidY(midY, exit.X, entry.X, obs, pad);

            // Шаг 2: обход выходного сегмента (exit.X, exit.Y→midY)
            // Возвращает (detourX, rejoinY) — X обходного коридора и Y возврата к exit.X,
            // гарантированно ниже препятствия, т.е. возврат идёт горизонтально не сквозь блок.
            var exitDet  = FindVerticalDetour(exit.X,  exit.Y, midY,    obs, pad, entry.X);

            // Шаг 3: обход входного сегмента (entry.X, midY→entry.Y)
            // rejoinY < entry.Y (для маршрута вниз) — после него идём вертикально
            // прямо к entry, т.е. стрелка ВСЕГДА подходит к блоку сверху, не сбоку.
            var entryDet = FindVerticalDetour(entry.X, midY,   entry.Y, obs, pad, exit.X);

            // Сборка маршрута
            // stub — короткий перпендикулярный отрезок сразу за границей блока,
            // чтобы горизонтальный поворот не проходил по самой границе блока.
            const double stub   = 12;
            double       stubY  = midY > exit.Y ? exit.Y + stub : exit.Y - stub;

            var route = new List<Point> { exit };
            if (exitDet != null)
            {
                // ↓ stub-отрезок ниже блока → горизонтальный поворот вне блока → ↓ до midY
                route.Add(new Point(exit.X,        stubY));  // ↓ ступенька
                route.Add(new Point(exitDet.Item1, stubY));  // → поворот на stub-уровне
                route.Add(new Point(exitDet.Item1, midY));   // ↓ до midY
            }
            else
            {
                route.Add(new Point(exit.X, midY));          // ↓ прямо до midY
            }
            route.Add(new Point(entry.X, midY));             // → горизонтальная перемычка

            if (entryDet != null)
            {
                // Для ВХОДНОГО сегмента возврат к entry.X нужен — чтобы стрелка
                // подходила к блоку строго сверху, а не сбоку.
                route.Add(new Point(entryDet.Item1, midY));           // → detour X
                route.Add(new Point(entryDet.Item1, entryDet.Item2)); // ↓ ниже препятствия
                route.Add(new Point(entry.X,        entryDet.Item2)); // ← к entry.X
            }
            route.Add(entry);   // ↓ вертикально к entry — стрелка всегда сверху

            return SimplifyRoute(route);
        }

        private static List<Point> BuildHorizontalRoute(Point exit, Point entry,
                                                         List<Node> obs, double pad)
        {
            // Шаг 1: найти midX — вертикальный сегмент без пересечений
            double midX = (exit.X + entry.X) / 2;
            midX = AdjustMidX(midX, exit.Y, entry.Y, obs, pad);

            var exitDet  = FindHorizontalDetour(exit.Y,  exit.X, midX,    obs, pad, entry.Y);
            var entryDet = FindHorizontalDetour(entry.Y, midX,   entry.X, obs, pad, exit.Y);

            const double hStub  = 12;
            double       stubX  = midX > exit.X ? exit.X + hStub : exit.X - hStub;

            var route = new List<Point> { exit };
            if (exitDet != null)
            {
                // → stub-отрезок правее блока → вертикальный поворот вне блока → → до midX
                route.Add(new Point(stubX, exit.Y));          // → ступенька
                route.Add(new Point(stubX, exitDet.Item1));   // ↑/↓ поворот на stub-уровне
                route.Add(new Point(midX,  exitDet.Item1));   // → до midX
            }
            else
            {
                route.Add(new Point(midX, exit.Y));
            }
            route.Add(new Point(midX, entry.Y));

            if (entryDet != null)
            {
                route.Add(new Point(midX,            entryDet.Item1)); // ↑/↓ detour Y
                route.Add(new Point(entryDet.Item2,  entryDet.Item1)); // → мимо блока
                route.Add(new Point(entryDet.Item2,  entry.Y));        // ↓/↑ к entry.Y
            }
            route.Add(entry);

            return SimplifyRoute(route);
        }

        // ─── Вспомогательные функции ─────────────────────────────────

        // Сдвигает midY так, чтобы горизонтальный сегмент [x1,x2] на Y=midY
        // не пересекал ни один блок.
        private static double AdjustMidY(double midY, double x1, double x2,
                                          List<Node> obs, double pad)
        {
            for (int i = 0; i < 6; i++)
            {
                Node b = FindOnHorizontal(x1, x2, midY, obs);
                if (b == null) break;
                double optAbove = b.Y - pad;
                double optBelow = b.Y + b.Height + pad;
                int cA = CountOnHorizontal(x1, x2, optAbove, obs);
                int cB = CountOnHorizontal(x1, x2, optBelow, obs);
                // При равенстве — ближе к исходному mid
                if (cA != cB) midY = cA <= cB ? optAbove : optBelow;
                else          midY = Math.Abs(optAbove - midY) <= Math.Abs(optBelow - midY)
                                     ? optAbove : optBelow;
            }
            return midY;
        }

        // Аналогично для вертикальной средней перемычки.
        private static double AdjustMidX(double midX, double y1, double y2,
                                          List<Node> obs, double pad)
        {
            for (int i = 0; i < 6; i++)
            {
                Node b = FindOnVertical(midX, y1, y2, obs);
                if (b == null) break;
                double optLeft  = b.X - pad;
                double optRight = b.X + b.Width + pad;
                int cL = CountOnVertical(optLeft,  y1, y2, obs);
                int cR = CountOnVertical(optRight, y1, y2, obs);
                if (cL != cR) midX = cL <= cR ? optLeft : optRight;
                else          midX = Math.Abs(optLeft - midX) <= Math.Abs(optRight - midX)
                                     ? optLeft : optRight;
            }
            return midX;
        }

        // Если вертикальный сегмент (x, y1→y2) пересекает один или несколько блоков —
        // возвращает Tuple(detourX, rejoinY), огибающий ВСЕ блоки одним U-образным обходом.
        ///
        // detourX — левее самого левого или правее самого правого из всех блоков на пути,
        //           поэтому вертикаль по detourX гарантированно чиста от всех них.
        // rejoinY — за ПОСЛЕДНИМ блоком в направлении движения (ниже самого нижнего
        //           при движении вниз), поэтому возврат к x не входит ни в один блок.
        private static Tuple<double, double> FindVerticalDetour(double x, double y1, double y2,
                                                                  List<Node> obs, double pad, double preferX)
        {
            double minY = Math.Min(y1, y2), maxY = Math.Max(y1, y2);
            var blockers = obs.Where(n => x > n.X && x < n.X + n.Width
                                       && maxY > n.Y && minY < n.Y + n.Height).ToList();
            if (blockers.Count == 0) return null;

            // Огибаем ВСЕ блоки: коридор левее самого левого или правее самого правого
            double optLeft  = blockers.Min(b => b.X) - pad;
            double optRight = blockers.Max(b => b.X + b.Width) + pad;
            int cL = CountOnVertical(optLeft,  y1, y2, obs);
            int cR = CountOnVertical(optRight, y1, y2, obs);
            // При равном числе пересечений используем направление движения как тайбрейкер:
            // вниз (y1 < y2) → вправо, вверх (y1 > y2) → влево.
            // Это гарантирует, что прямые и обратные связи между одними блоками
            // идут по разным сторонам и не накладываются друг на друга.
            bool goingDown = y1 < y2;
            double detourX = (cL != cR)
                ? (cL < cR ? optLeft : optRight)
                : goingDown ? optRight : optLeft;

            // rejoinY — за ПОСЛЕДНИМ блоком по направлению движения
            double rejoinY = y1 < y2
                ? blockers.Max(b => b.Y + b.Height) + pad   // ниже самого нижнего
                : blockers.Min(b => b.Y) - pad;              // выше самого верхнего
            return Tuple.Create(detourX, rejoinY);
        }

        // Если горизонтальный сегмент (y, x1→x2) пересекает один или несколько блоков —
        // возвращает Tuple(detourY, rejoinX), огибающий ВСЕ блоки одним обходом.
        private static Tuple<double, double> FindHorizontalDetour(double y, double x1, double x2,
                                                                    List<Node> obs, double pad, double preferY)
        {
            double minX = Math.Min(x1, x2), maxX = Math.Max(x1, x2);
            var blockers = obs.Where(n => y > n.Y && y < n.Y + n.Height
                                       && maxX > n.X && minX < n.X + n.Width).ToList();
            if (blockers.Count == 0) return null;

            double optTop    = blockers.Min(b => b.Y) - pad;
            double optBottom = blockers.Max(b => b.Y + b.Height) + pad;
            int cT = CountOnHorizontal(x1, x2, optTop,    obs);
            int cB = CountOnHorizontal(x1, x2, optBottom, obs);
            // вправо (x1 < x2) → снизу; влево → сверху
            bool goingRight = x1 < x2;
            double detourY = (cT != cB)
                ? (cT < cB ? optTop : optBottom)
                : goingRight ? optBottom : optTop;
            double rejoinX = x1 < x2
                ? blockers.Max(b => b.X + b.Width) + pad
                : blockers.Min(b => b.X) - pad;
            return Tuple.Create(detourY, rejoinX);
        }

        // ── Примитивы: строгое пересечение без отступа ───────────────

        private static Node FindOnVertical(double x, double y1, double y2, List<Node> obs)
        {
            double minY = Math.Min(y1, y2), maxY = Math.Max(y1, y2);
            foreach (var n in obs)
                if (x > n.X && x < n.X + n.Width && maxY > n.Y && minY < n.Y + n.Height)
                    return n;
            return null;
        }

        private static Node FindOnHorizontal(double x1, double x2, double y, List<Node> obs)
        {
            double minX = Math.Min(x1, x2), maxX = Math.Max(x1, x2);
            foreach (var n in obs)
                if (y > n.Y && y < n.Y + n.Height && maxX > n.X && minX < n.X + n.Width)
                    return n;
            return null;
        }

        private static int CountOnVertical(double x, double y1, double y2, List<Node> obs)
        {
            double minY = Math.Min(y1, y2), maxY = Math.Max(y1, y2);
            int c = 0;
            foreach (var n in obs)
                if (x > n.X && x < n.X + n.Width && maxY > n.Y && minY < n.Y + n.Height) c++;
            return c;
        }

        private static int CountOnHorizontal(double x1, double x2, double y, List<Node> obs)
        {
            double minX = Math.Min(x1, x2), maxX = Math.Max(x1, x2);
            int c = 0;
            foreach (var n in obs)
                if (y > n.Y && y < n.Y + n.Height && maxX > n.X && minX < n.X + n.Width) c++;
            return c;
        }

        // Маршрут через промежуточные точки UserPoints.
        private List<Point> BuildRouteWithWaypoints(Node source, Node target)
        {
            Point first = UserPoints[0], last = UserPoints[UserPoints.Count - 1];
            var route = new List<Point> { GetEdgePoint(source, first.X, first.Y) };
            foreach (var p in UserPoints) route.Add(p);
            route.Add(GetEdgePoint(target, last.X, last.Y));
            return route;
        }

        // Удаляет точки, лежащие на одной прямой с соседними.
        private static List<Point> SimplifyRoute(List<Point> pts)
        {
            if (pts.Count <= 2) return pts;
            var result = new List<Point> { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Point prev = result[result.Count - 1], curr = pts[i], next = pts[i + 1];
                bool col = (Math.Abs(prev.X - curr.X) < 0.5 && Math.Abs(curr.X - next.X) < 0.5)
                        || (Math.Abs(prev.Y - curr.Y) < 0.5 && Math.Abs(curr.Y - next.Y) < 0.5);
                if (!col) result.Add(curr);
            }
            result.Add(pts[pts.Count - 1]);
            return result;
        }

        private void DrawPolyline(List<Point> points)
        {
            if (points.Count < 2) return;
            var figure = new PathFigure { StartPoint = points[0] };
            for (int i = 1; i < points.Count; i++)
                figure.Segments.Add(new LineSegment(points[i], true));
            var geo = new PathGeometry();
            geo.Figures.Add(figure);
            PathElement.Data = geo;
            ArrowHead.Data   = BuildArrow(points[points.Count - 2], points[points.Count - 1]);

            // Позиционируем метку в середине маршрута (чуть выше линии)
            if (LabelElement != null && !string.IsNullOrEmpty(Label))
            {
                Point mid = GetRouteMidpoint(points);
                System.Windows.Controls.Canvas.SetLeft(LabelElement, mid.X + 4);
                System.Windows.Controls.Canvas.SetTop(LabelElement,  mid.Y - 16);
            }
        }

        private static Point GetRouteMidpoint(List<Point> pts)
        {
            double total = 0;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                double dx = pts[i+1].X - pts[i].X, dy = pts[i+1].Y - pts[i].Y;
                total += Math.Sqrt(dx*dx + dy*dy);
            }
            double half = total / 2, acc = 0;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                double dx = pts[i+1].X - pts[i].X, dy = pts[i+1].Y - pts[i].Y;
                double len = Math.Sqrt(dx*dx + dy*dy);
                if (acc + len >= half)
                {
                    double t = len > 0 ? (half - acc) / len : 0;
                    return new Point(pts[i].X + t * dx, pts[i].Y + t * dy);
                }
                acc += len;
            }
            return pts[pts.Count / 2];
        }

        private Geometry BuildArrow(Point from, Point to)
        {
            const double L = 8, W = 4;
            double angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
            double bx = to.X - L * Math.Cos(angle), by = to.Y - L * Math.Sin(angle);
            double lx = bx + W * Math.Sin(angle),   ly = by - W * Math.Cos(angle);
            double rx = bx - W * Math.Sin(angle),   ry = by + W * Math.Cos(angle);
            var fig = new PathFigure { StartPoint = to, IsClosed = true, IsFilled = true };
            fig.Segments.Add(new LineSegment(new Point(lx, ly), true));
            fig.Segments.Add(new LineSegment(new Point(rx, ry), true));
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            return geo;
        }

        // Точка на краю прямоугольника блока, ближайшая к (ox, oy).
        // Используется для точек выхода/входа связи.
        public Point GetEdgePoint(Node node, double ox, double oy)
        {
            double cx = node.X + node.Width / 2, cy = node.Y + node.Height / 2;
            double dx = ox - cx, dy = oy - cy;
            if (dx == 0 && dy == 0) return new Point(cx, cy);
            double tX = Math.Abs(dx) > 0.001 ? (node.Width  / 2) / Math.Abs(dx) : double.MaxValue;
            double tY = Math.Abs(dy) > 0.001 ? (node.Height / 2) / Math.Abs(dy) : double.MaxValue;
            double t  = Math.Min(tX, tY);
            return new Point(cx + dx * t, cy + dy * t);
        }

        // Хит-тест: попадает ли точка в зону клика связи.
        public bool HitTest(Point point, double tolerance = 8)
        {
            if (PathElement?.Data == null) return false;
            var geo = PathElement.Data as PathGeometry;
            if (geo == null) return false;
            return geo.GetWidenedPathGeometry(new Pen(Brushes.Black, tolerance * 2)).FillContains(point);
        }

        // Читаемое название типа для UI.
        public string TypeLabel =>
            _type == "relational"  ? "Реляционная (пунктир)"   :
            _type == "object-flow" ? "Поток объектов (точки)"   :
                                     "Предшествования (сплошная)";
    }
}
