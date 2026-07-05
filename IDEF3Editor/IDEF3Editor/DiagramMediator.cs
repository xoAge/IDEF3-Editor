using System.Collections.Generic;
using System.Windows.Controls;

namespace IDEF3Editor
{
    // Посредник (Mediator), предоставляющий командам отмены/повтора доступ
    // к операциям с холстом без прямой ссылки на MainWindow.
    // MainWindow создаёт единственный экземпляр и передаёт его каждой команде.
    public class DiagramMediator
    {
        private readonly Canvas     _canvas; // рабочий холст DrawingCanvas
        private readonly List<Node> _nodes;  // список всех блоков диаграммы
        private readonly List<Link> _links;  // список всех связей диаграммы

        public DiagramMediator(Canvas canvas, List<Node> nodes, List<Link> links)
        {
            _canvas = canvas;
            _nodes  = nodes;
            _links  = links;
        }

        // ─── Операции с блоками ───────────────────────────────────────

        // Добавляет блок в список данных и на холст.
        // После добавления обновляет числовую метку и координаты.
        // При удалении и повторном добавлении (undo/redo) Canvas теряет
        // прикреплённые свойства — UpdatePosition() их восстанавливает.
        public void AddNodeToCanvas(Node node)
        {
            if (!_nodes.Contains(node)) _nodes.Add(node);

            if (!_canvas.Children.Contains(node.MainShape))
                _canvas.Children.Add(node.MainShape);
            if (node.JLineRight   != null && !_canvas.Children.Contains(node.JLineRight))
                _canvas.Children.Add(node.JLineRight);
            if (node.JLineLeft    != null && !_canvas.Children.Contains(node.JLineLeft))
                _canvas.Children.Add(node.JLineLeft);
            if (node.BottomLine   != null && !_canvas.Children.Contains(node.BottomLine))
                _canvas.Children.Add(node.BottomLine);
            if (node.StripDivider != null && !_canvas.Children.Contains(node.StripDivider))
                _canvas.Children.Add(node.StripDivider);
            if (!_canvas.Children.Contains(node.TextBlock))
                _canvas.Children.Add(node.TextBlock);
            if (node.NumberText   != null && !_canvas.Children.Contains(node.NumberText))
                _canvas.Children.Add(node.NumberText);
            if (node.RefText      != null && !_canvas.Children.Contains(node.RefText))
                _canvas.Children.Add(node.RefText);
            if (node.ResizeHandle != null && !_canvas.Children.Contains(node.ResizeHandle))
                _canvas.Children.Add(node.ResizeHandle);

            // Синхронизируем метки (после undo значения могли измениться)
            node.RefreshNumber();
            node.RefreshReference();

            // Восстанавливаем Canvas.SetLeft/SetTop (при повторном добавлении они обнуляются)
            node.UpdatePosition();
        }

        // Удаляет все визуальные части блока с холста и из списка данных.
        public void RemoveNodeFromCanvas(Node node, bool removingForUndo)
        {
            _canvas.Children.Remove(node.MainShape);
            if (node.JLineRight   != null) _canvas.Children.Remove(node.JLineRight);
            if (node.JLineLeft    != null) _canvas.Children.Remove(node.JLineLeft);
            if (node.BottomLine   != null) _canvas.Children.Remove(node.BottomLine);
            if (node.StripDivider != null) _canvas.Children.Remove(node.StripDivider);
            _canvas.Children.Remove(node.TextBlock);
            if (node.NumberText   != null) _canvas.Children.Remove(node.NumberText);
            if (node.RefText      != null) _canvas.Children.Remove(node.RefText);
            if (node.ResizeHandle != null) _canvas.Children.Remove(node.ResizeHandle);
            _nodes.Remove(node);
        }

        // ─── Операции со связями ──────────────────────────────────────

        // Добавляет связь в список данных и на холст.
        // Связи вставляются в начало коллекции, чтобы отрисовываться ниже блоков.
        public void AddLinkToCanvas(Link link)
        {
            if (!_links.Contains(link)) _links.Add(link);

            if (!_canvas.Children.Contains(link.PathElement))
                _canvas.Children.Insert(0, link.PathElement);
            if (!_canvas.Children.Contains(link.ArrowHead))
                _canvas.Children.Insert(0, link.ArrowHead);
            // Метка добавляется поверх линии (не в начало, а в конец)
            if (link.LabelElement != null && !_canvas.Children.Contains(link.LabelElement))
                _canvas.Children.Add(link.LabelElement);
        }

        // Удаляет визуальные части связи с холста и из списка данных.
        public void RemoveLinkFromCanvas(Link link)
        {
            _canvas.Children.Remove(link.PathElement);
            _canvas.Children.Remove(link.ArrowHead);
            if (link.LabelElement != null) _canvas.Children.Remove(link.LabelElement);
            _links.Remove(link);
        }

        // Пересчитывает и перерисовывает маршрут связи.
        // Передаёт список всех блоков для обхода препятствий.
        public void RefreshLink(Link link)
        {
            Node src = _nodes.Find(n => n.Id == link.SourceId);
            Node tgt = _nodes.Find(n => n.Id == link.TargetId);
            if (src != null && tgt != null)
                link.UpdatePosition(src, tgt, _nodes, _links);
        }

        // ─── Трансформации блоков ─────────────────────────────────────

        // Перемещает блок в позицию (x, y) и обновляет связанные связи.
        public void MoveNode(Node node, double x, double y)
        {
            node.X = x; node.Y = y;
            node.UpdatePosition();
            RefreshAllLinksForNode(node);
        }

        // Изменяет размер блока и обновляет связанные связи.
        public void ResizeNode(Node node, double w, double h)
        {
            node.Width  = w; node.Height = h;
            node.MainShape.Width  = w; node.MainShape.Height = h;
            node.UpdatePosition();
            RefreshAllLinksForNode(node);
        }

        private void RefreshAllLinksForNode(Node node)
        {
            foreach (var link in _links)
                if (link.SourceId == node.Id || link.TargetId == node.Id)
                    RefreshLink(link);
        }

        // ─── Навигация по декомпозициям ───────────────────────────────

        // Добавляет только визуальные части блока на холст, не трогая список _nodes.
        // Используется при переходе между уровнями декомпозиции.
        public void AddNodeVisualsOnly(Node node)
        {
            if (!_canvas.Children.Contains(node.MainShape))
                _canvas.Children.Add(node.MainShape);
            if (node.JLineRight   != null && !_canvas.Children.Contains(node.JLineRight))
                _canvas.Children.Add(node.JLineRight);
            if (node.JLineLeft    != null && !_canvas.Children.Contains(node.JLineLeft))
                _canvas.Children.Add(node.JLineLeft);
            if (node.BottomLine   != null && !_canvas.Children.Contains(node.BottomLine))
                _canvas.Children.Add(node.BottomLine);
            if (node.StripDivider != null && !_canvas.Children.Contains(node.StripDivider))
                _canvas.Children.Add(node.StripDivider);
            if (!_canvas.Children.Contains(node.TextBlock))
                _canvas.Children.Add(node.TextBlock);
            if (node.NumberText   != null && !_canvas.Children.Contains(node.NumberText))
                _canvas.Children.Add(node.NumberText);
            if (node.RefText      != null && !_canvas.Children.Contains(node.RefText))
                _canvas.Children.Add(node.RefText);
            if (node.ResizeHandle != null && !_canvas.Children.Contains(node.ResizeHandle))
                _canvas.Children.Add(node.ResizeHandle);
            node.RefreshNumber();
            node.RefreshReference();
            node.UpdatePosition();
        }

        // Удаляет только визуальные части блока с холста, не трогая список _nodes.
        public void RemoveNodeVisualsOnly(Node node)
        {
            _canvas.Children.Remove(node.MainShape);
            if (node.JLineRight   != null) _canvas.Children.Remove(node.JLineRight);
            if (node.JLineLeft    != null) _canvas.Children.Remove(node.JLineLeft);
            if (node.BottomLine   != null) _canvas.Children.Remove(node.BottomLine);
            if (node.StripDivider != null) _canvas.Children.Remove(node.StripDivider);
            _canvas.Children.Remove(node.TextBlock);
            if (node.NumberText   != null) _canvas.Children.Remove(node.NumberText);
            if (node.RefText      != null) _canvas.Children.Remove(node.RefText);
            if (node.ResizeHandle != null) _canvas.Children.Remove(node.ResizeHandle);
        }

        // Добавляет только визуальные части связи, не трогая список _links.
        public void AddLinkVisualsOnly(Link link)
        {
            if (!_canvas.Children.Contains(link.PathElement))
                _canvas.Children.Insert(0, link.PathElement);
            if (!_canvas.Children.Contains(link.ArrowHead))
                _canvas.Children.Insert(0, link.ArrowHead);
            if (link.LabelElement != null && !_canvas.Children.Contains(link.LabelElement))
                _canvas.Children.Add(link.LabelElement);
        }

        // Удаляет только визуальные части связи, не трогая список _links.
        public void RemoveLinkVisualsOnly(Link link)
        {
            _canvas.Children.Remove(link.PathElement);
            _canvas.Children.Remove(link.ArrowHead);
            if (link.LabelElement != null) _canvas.Children.Remove(link.LabelElement);
        }
    }
}
