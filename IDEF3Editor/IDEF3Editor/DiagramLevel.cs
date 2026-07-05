using System.Collections.Generic;

namespace IDEF3Editor
{
    // Состояние одного уровня диаграммы — либо корневого, либо декомпозиции UOB-блока.
    // Содержит блоки, связи, историю отмены и сохранённую позицию вида.
    public class DiagramLevel
    {
        public string          LevelId       { get; set; } = System.Guid.NewGuid().ToString();
        public string          Name          { get; set; } = "";
        public string          ParentBlockId { get; set; } = ""; // ID блока, которому принадлежит этот уровень

        public List<Node>      Nodes         { get; set; } = new List<Node>();
        public List<Link>      Links         { get; set; } = new List<Link>();
        public UndoRedoManager History       { get; set; } = new UndoRedoManager();

        // Сохранённое состояние вида (восстанавливается при возврате на уровень)
        public double Zoom { get; set; } = 1.0;
        public double PanX { get; set; } = 0;
        public double PanY { get; set; } = 0;
    }
}
