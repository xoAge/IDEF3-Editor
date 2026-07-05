using System;
using System.Collections.Generic;

namespace IDEF3Editor
{
    // ─── Базовый интерфейс команды ────────────────────────────────────

    // Интерфейс обратимой команды. Каждое действие пользователя
    // оборачивается в команду, реализующую этот интерфейс.
    public interface IUndoableCommand
    {
        void   Execute();
        void   Undo();
        string Description { get; } // краткое описание для строки состояния
    }

    // ─── Менеджер стека отмены / повтора ─────────────────────────────

    // Управляет двумя стеками: отмены и повтора.
    // При выполнении новой команды стек повтора очищается.
    public class UndoRedoManager
    {
        private readonly Stack<IUndoableCommand> _undoStack = new Stack<IUndoableCommand>();
        private readonly Stack<IUndoableCommand> _redoStack = new Stack<IUndoableCommand>();

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public string UndoDescription => CanUndo ? _undoStack.Peek().Description : "";
        public string RedoDescription => CanRedo ? _redoStack.Peek().Description : "";

        // Вызывается при любом изменении стеков — для обновления кнопок UI.
        public event EventHandler StackChanged;

        // Выполняет команду и помещает на стек отмены. Стек повтора очищается.
        public void Execute(IUndoableCommand command)
        {
            command.Execute();
            _undoStack.Push(command);
            _redoStack.Clear();
            StackChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Undo()
        {
            if (!CanUndo) return;
            var cmd = _undoStack.Pop();
            cmd.Undo();
            _redoStack.Push(cmd);
            StackChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Redo()
        {
            if (!CanRedo) return;
            var cmd = _redoStack.Pop();
            cmd.Execute();
            _undoStack.Push(cmd);
            StackChanged?.Invoke(this, EventArgs.Empty);
        }

        // Помещает команду на стек без вызова Execute().
        // Для операций, уже применённых визуально (например, перетаскивание).
        public void PushAlreadyExecuted(IUndoableCommand command)
        {
            _undoStack.Push(command);
            _redoStack.Clear();
            StackChanged?.Invoke(this, EventArgs.Empty);
        }

        // Очищает оба стека (при открытии новой диаграммы).
        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            StackChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ════════════════════════════════════════════════════════════════
    // КОНКРЕТНЫЕ КОМАНДЫ
    // ════════════════════════════════════════════════════════════════

    // Добавление блока на диаграмму.
    public class AddNodeCommand : IUndoableCommand
    {
        private readonly Node            _node;
        private readonly DiagramMediator _mediator;
        public string Description => $"Добавить блок «{_node.Text}»";
        public AddNodeCommand(Node node, DiagramMediator mediator) { _node = node; _mediator = mediator; }
        public void Execute() => _mediator.AddNodeToCanvas(_node);
        public void Undo()    => _mediator.RemoveNodeFromCanvas(_node, removingForUndo: true);
    }

    // Удаление блока и всех его связей.
    public class RemoveNodeCommand : IUndoableCommand
    {
        private readonly Node            _node;
        private readonly List<Link>      _removedLinks;
        private readonly DiagramMediator _mediator;
        public string Description => $"Удалить блок «{_node.Text}»";
        public RemoveNodeCommand(Node node, List<Link> connectedLinks, DiagramMediator mediator)
        {
            _node = node; _removedLinks = new List<Link>(connectedLinks); _mediator = mediator;
        }
        public void Execute()
        {
            foreach (var link in _removedLinks) _mediator.RemoveLinkFromCanvas(link);
            _mediator.RemoveNodeFromCanvas(_node, removingForUndo: false);
        }
        public void Undo()
        {
            _mediator.AddNodeToCanvas(_node);
            foreach (var link in _removedLinks) { _mediator.AddLinkToCanvas(link); _mediator.RefreshLink(link); }
        }
    }

    // Добавление связи.
    public class AddLinkCommand : IUndoableCommand
    {
        private readonly Link            _link;
        private readonly DiagramMediator _mediator;
        public string Description => "Добавить связь";
        public AddLinkCommand(Link link, DiagramMediator mediator) { _link = link; _mediator = mediator; }
        public void Execute() { _mediator.AddLinkToCanvas(_link); _mediator.RefreshLink(_link); }
        public void Undo()    => _mediator.RemoveLinkFromCanvas(_link);
    }

    // Удаление связи.
    public class RemoveLinkCommand : IUndoableCommand
    {
        private readonly Link            _link;
        private readonly DiagramMediator _mediator;
        public string Description => "Удалить связь";
        public RemoveLinkCommand(Link link, DiagramMediator mediator) { _link = link; _mediator = mediator; }
        public void Execute() => _mediator.RemoveLinkFromCanvas(_link);
        public void Undo()    { _mediator.AddLinkToCanvas(_link); _mediator.RefreshLink(_link); }
    }

    // Перемещение одного блока.
    public class MoveNodeCommand : IUndoableCommand
    {
        private readonly Node            _node;
        private readonly double          _oldX, _oldY, _newX, _newY;
        private readonly DiagramMediator _mediator;
        public string Description => $"Переместить «{_node.Text}»";
        public MoveNodeCommand(Node node, double oldX, double oldY,
                               double newX, double newY, DiagramMediator mediator)
        {
            _node = node; _oldX = oldX; _oldY = oldY; _newX = newX; _newY = newY; _mediator = mediator;
        }
        public void Execute() => _mediator.MoveNode(_node, _newX, _newY);
        public void Undo()    => _mediator.MoveNode(_node, _oldX, _oldY);
    }

    // Изменение размера блока.
    public class ResizeNodeCommand : IUndoableCommand
    {
        private readonly Node            _node;
        private readonly double          _oldW, _oldH, _newW, _newH;
        private readonly DiagramMediator _mediator;
        public string Description => $"Изменить размер «{_node.Text}»";
        public ResizeNodeCommand(Node node, double oldW, double oldH,
                                 double newW, double newH, DiagramMediator mediator)
        {
            _node = node; _oldW = oldW; _oldH = oldH; _newW = newW; _newH = newH; _mediator = mediator;
        }
        public void Execute() => _mediator.ResizeNode(_node, _newW, _newH);
        public void Undo()    => _mediator.ResizeNode(_node, _oldW, _oldH);
    }

    // Одновременное перемещение нескольких блоков.
    // Все смещения объединены в одну запись стека.
    public class MoveMultipleNodesCommand : IUndoableCommand
    {
        public string Description => "Переместить группу";
        private readonly List<(Node node, double oldX, double oldY, double newX, double newY)> _moves;
        private readonly DiagramMediator _mediator;
        public MoveMultipleNodesCommand(
            IEnumerable<(Node, double, double, double, double)> moves, DiagramMediator mediator)
        {
            _moves = new List<(Node, double, double, double, double)>(moves); _mediator = mediator;
        }
        public void Execute() { foreach (var m in _moves) _mediator.MoveNode(m.node, m.newX, m.newY); }
        public void Undo()    { foreach (var m in _moves) _mediator.MoveNode(m.node, m.oldX, m.oldY); }
    }

    // Удаление нескольких блоков и всех их связей за одно действие.
    public class RemoveMultipleNodesCommand : IUndoableCommand
    {
        public string Description => "Удалить группу";
        private readonly List<(Node node, List<Link> links)> _removed;
        private readonly DiagramMediator _mediator;
        public RemoveMultipleNodesCommand(
            IEnumerable<(Node, List<Link>)> items, DiagramMediator mediator)
        {
            _removed = new List<(Node, List<Link>)>(items); _mediator = mediator;
        }
        public void Execute()
        {
            // Дедупликация: одна связь может соединять два удаляемых блока
            var allLinks = new List<Link>();
            foreach (var item in _removed)
                foreach (var link in item.links)
                    if (!allLinks.Contains(link)) allLinks.Add(link);
            foreach (var link in allLinks)     _mediator.RemoveLinkFromCanvas(link);
            foreach (var (node, _) in _removed) _mediator.RemoveNodeFromCanvas(node, removingForUndo: false);
        }
        public void Undo()
        {
            foreach (var (node, links) in _removed)
            {
                _mediator.AddNodeToCanvas(node);
                foreach (var link in links) { _mediator.AddLinkToCanvas(link); _mediator.RefreshLink(link); }
            }
        }
    }

    // Изменение типа связи.
    // Сеттер Link.Type вызывает ApplyTypeStyle() — визуал обновляется автоматически.
    public class ChangeLinkTypeCommand : IUndoableCommand
    {
        private readonly Link   _link;
        private readonly string _oldType, _newType;
        public string Description => "Изменить тип связи";
        public ChangeLinkTypeCommand(Link link, string oldType, string newType)
        { _link = link; _oldType = oldType; _newType = newType; }
        public void Execute() => _link.Type = _newType;
        public void Undo()    => _link.Type = _oldType;
    }

    // Редактирование текстового названия UOB-блока.
    // Хранит старое и новое значения для корректного undo/redo.
    public class EditTextCommand : IUndoableCommand
    {
        private readonly Node   _node;
        private readonly string _oldText, _newText;
        public string Description => "Редактировать текст";
        public EditTextCommand(Node node, string oldText, string newText)
        { _node = node; _oldText = oldText; _newText = newText; }
        public void Execute() => Apply(_newText);
        public void Undo()    => Apply(_oldText);
        private void Apply(string text)
        {
            _node.Text = text;
            _node.RefreshText();
        }
    }

    // Изменение порядкового номера блока или перекрёстка с поддержкой отмены.
    public class ChangeNumberCommand : IUndoableCommand
    {
        private readonly Node _node;
        private readonly int  _oldNumber, _newNumber;
        public string Description => "Изменить номер";
        public ChangeNumberCommand(Node node, int oldNumber, int newNumber)
        { _node = node; _oldNumber = oldNumber; _newNumber = newNumber; }
        public void Execute() => Apply(_newNumber);
        public void Undo()    => Apply(_oldNumber);
        private void Apply(int number) { _node.Number = number; _node.RefreshNumber(); }
    }

    // Изменение ссылочного выражения в правой ячейке UOB-блока.
    public class ChangeReferenceCommand : IUndoableCommand
    {
        private readonly Node   _node;
        private readonly string _oldRef, _newRef;
        public string Description => "Изменить ссылку";
        public ChangeReferenceCommand(Node node, string oldRef, string newRef)
        { _node = node; _oldRef = oldRef; _newRef = newRef; }
        public void Execute() => Apply(_newRef);
        public void Undo()    => Apply(_oldRef);
        private void Apply(string r) { _node.ReferenceExpression = r; _node.RefreshReference(); }
    }

    // Изменение подписи на линии связи.
    public class ChangeLinkLabelCommand : IUndoableCommand
    {
        private readonly Link   _link;
        private readonly string _oldLabel, _newLabel;
        public string Description => "Изменить подпись связи";
        public ChangeLinkLabelCommand(Link link, string oldLabel, string newLabel)
        { _link = link; _oldLabel = oldLabel; _newLabel = newLabel; }
        public void Execute() => Apply(_newLabel);
        public void Undo()    => Apply(_oldLabel);
        private void Apply(string l) { _link.Label = l; _link.RefreshLabel(); }
    }

    // Вставка скопированных блоков и связей между ними.
    public class PasteCommand : IUndoableCommand
    {
        private readonly List<Node>      _nodes;
        private readonly List<Link>      _links;
        private readonly DiagramMediator _mediator;
        public string Description => "Вставить";
        public PasteCommand(List<Node> nodes, List<Link> links, DiagramMediator mediator)
        { _nodes = nodes; _links = links; _mediator = mediator; }
        public void Execute()
        {
            foreach (var n in _nodes) _mediator.AddNodeToCanvas(n);
            foreach (var l in _links) { _mediator.AddLinkToCanvas(l); _mediator.RefreshLink(l); }
        }
        public void Undo()
        {
            foreach (var l in _links) _mediator.RemoveLinkFromCanvas(l);
            foreach (var n in _nodes) _mediator.RemoveNodeFromCanvas(n, true);
        }
    }
}
