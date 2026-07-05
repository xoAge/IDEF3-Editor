using System;
using System.Collections.Generic;
using System.Windows;

namespace IDEF3Editor
{
    // Модель сохранения одного блока диаграммы.
    public class SaveNode
    {
        public string Id                  { get; set; }
        public string Type                { get; set; }
        public double X                   { get; set; }
        public double Y                   { get; set; }
        public double Width               { get; set; }
        public double Height              { get; set; }
        public string Text                { get; set; }
        public int    Number              { get; set; }
        public string Description         { get; set; }
        public bool   IsStartNode         { get; set; }
        public string JunctionType        { get; set; }
        public string ReferenceExpression { get; set; } // правая ячейка нижней полосы UOB
    }

    // Модель сохранения одной связи диаграммы.
    public class SaveLink
    {
        public string       Id       { get; set; }
        public string       SourceId { get; set; }
        public string       TargetId { get; set; }
        public string       Type     { get; set; }
        public string       Label    { get; set; } // подпись на линии связи
        public List<Point>  Points   { get; set; }
    }

    // Корневая модель сохранения всей диаграммы.
    // Сериализуется в JSON-файл с расширением .idef3.
    public class SaveDiagram
    {
        public string         Id          { get; set; }
        public string         Name        { get; set; }
        public string         Description { get; set; }
        public DateTime       Created     { get; set; }
        public DateTime       Modified    { get; set; }
        public string         Author      { get; set; }
        public double         Scale       { get; set; }
        public List<SaveNode> Nodes       { get; set; }
        public List<SaveLink> Links       { get; set; }

        // Плоский список декомпозиций: каждая запись хранит дочернюю диаграмму
        // для конкретного UOB-блока (по ParentBlockId).
        // Плоская структура поддерживает любую глубину вложенности,
        // так как ID блоков глобально уникальны (GUID).
        public List<SaveChildDiagram> Decompositions { get; set; }
    }

    // Декомпозиция одного UOB-блока — дочерняя диаграмма.
    public class SaveChildDiagram
    {
        public string         Id            { get; set; }
        public string         ParentBlockId { get; set; } // ID блока-хозяина
        public string         Name          { get; set; }
        public List<SaveNode> Nodes         { get; set; }
        public List<SaveLink> Links         { get; set; }
    }
}
