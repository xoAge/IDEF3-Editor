using System.Collections.Generic;
using System.Linq;

namespace IDEF3Editor
{
    public enum ValidationSeverity { Error, Warning, Info }

    // Одно замечание по результатам проверки диаграммы.
    // Может быть привязано к конкретному блоку (NodeId != null).
    public class ValidationIssue
    {
        public ValidationSeverity Severity { get; set; }
        public string             Message  { get; set; }
        public string             NodeId   { get; set; } // null = замечание уровня диаграммы
        public string Icon =>
            Severity == ValidationSeverity.Error   ? "✕" :
            Severity == ValidationSeverity.Warning ? "⚠" : "ℹ";
    }

    // Валидатор диаграммы IDEF3 PFD.
    // Проверяет структуру и элементы диаграммы по правилам нотации.
    public static class Idef3Validator
    {
        // Выполняет полную проверку диаграммы и возвращает список замечаний,
        // отсортированных по серьёзности (ошибки → предупреждения → информация).
        public static List<ValidationIssue> Validate(List<Node> nodes, List<Link> links)
        {
            var issues = new List<ValidationIssue>();

            // Пустая диаграмма
            if (nodes.Count == 0)
            {
                issues.Add(Err("Диаграмма пуста — добавьте хотя бы один UOB-блок."));
                return issues;
            }

            var uobs      = nodes.Where(n => !n.IsJunction).ToList();
            var junctions = nodes.Where(n => n.IsJunction).ToList();

            if (uobs.Count == 0)
                issues.Add(Err("Диаграмма не содержит ни одного UOB-блока."));

            if (links.Count == 0 && nodes.Count > 1)
                issues.Add(Warn("Диаграмма содержит блоки, но не содержит ни одной связи."));

            // Проверка связности — нет ли изолированных блоков
            if (nodes.Count > 1)
            {
                var reached = Reachable(nodes, links);
                if (reached.Count < nodes.Count)
                    issues.Add(Warn(
                        $"Диаграмма содержит несвязные компоненты: " +
                        $"{nodes.Count - reached.Count} блок(ов) не соединены с остальными."));
            }

            // Проверка каждого блока
            foreach (var node in nodes)
            {
                var inLinks  = links.Where(l => l.TargetId == node.Id).ToList();
                var outLinks = links.Where(l => l.SourceId == node.Id).ToList();
                if (node.IsJunction) ValidateJunction(node, inLinks, outLinks, issues);
                else                 ValidateUob(node, inLinks, outLinks, uobs.Count, issues);
            }

            // Проверка связей
            var seen = new HashSet<string>();
            foreach (var link in links)
            {
                var src = nodes.FirstOrDefault(n => n.Id == link.SourceId);
                var tgt = nodes.FirstOrDefault(n => n.Id == link.TargetId);

                if (src == null || tgt == null)
                { issues.Add(Err("Обнаружена связь с отсутствующим источником или целью.")); continue; }

                // Дублирующие связи между теми же блоками
                string key = link.SourceId + "→" + link.TargetId;
                if (!seen.Add(key))
                    issues.Add(Warn(
                        $"Дублирующая связь между блоками #{src.Number} и #{tgt.Number}. Удалите лишнюю."));

                // Поток объектов между двумя UOB (допустимо, но нетипично)
                if (link.Type == "object-flow" && !src.IsJunction && !tgt.IsJunction)
                    issues.Add(Info(
                        $"Связь «Поток объектов» между UOB-блоками #{src.Number} и #{tgt.Number}. " +
                        $"Обычно такие связи проходят через перекрёстки."));

                // Реляционная связь не должна соединять два перекрёстка
                if (link.Type == "relational" && src.IsJunction && tgt.IsJunction)
                    issues.Add(Warn(
                        $"Реляционная связь между двумя перекрёстками " +
                        $"(#{src.Number} → #{tgt.Number}). Реляционные связи предназначены для UOB-блоков."));
            }

            // Итоговая статистика
            issues.Add(Info(
                $"Итого: {uobs.Count} UOB-блок(ов), {junctions.Count} перекрёсток(ов), {links.Count} связь(ей)."));

            return issues.OrderBy(i => (int)i.Severity).ToList();
        }

        // ─── Проверка перекрёстков ────────────────────────────────────

        private static void ValidateJunction(
            Node node, List<Link> inLinks, List<Link> outLinks, List<ValidationIssue> issues)
        {
            int fanIn = inLinks.Count, fanOut = outLinks.Count;
            string name = JunctionName(node.Type);

            // Перекрёсток должен иметь не менее двух связей суммарно
            if (fanIn + fanOut < 2)
                issues.Add(Err(
                    $"Перекрёсток {name} #{node.Number}: менее 2 связей. " +
                    $"Перекрёсток должен объединять минимум 2 потока.", node.Id));

            // Ровно 1 вход и 1 выход — перекрёсток избыточен, не выполняет функцию ветвления/слияния
            if (fanIn == 1 && fanOut == 1)
                issues.Add(Warn(
                    $"Перекрёсток {name} #{node.Number} имеет 1 входящую и 1 исходящую связь. " +
                    $"Перекрёсток не выполняет функцию ветвления или слияния потоков — он избыточен. " +
                    $"Удалите перекрёсток или добавьте дополнительные связи.", node.Id));

            // Только fan-out без входящих
            if (fanIn == 0 && fanOut >= 2)
                issues.Add(Info(
                    $"Перекрёсток {name} #{node.Number} работает как точка ветвления (fan-out) " +
                    $"без входящих связей. Убедитесь, что это намеренно.", node.Id));

            // Только fan-in без исходящих
            if (fanOut == 0 && fanIn >= 2)
                issues.Add(Info(
                    $"Перекрёсток {name} #{node.Number} работает как точка слияния (fan-in) " +
                    $"без исходящих связей. Убедитесь, что это намеренно.", node.Id));

            // XOR: fan-out должен быть ровно 1 (исключающий выбор)
            if (node.Type == "junction-xor" && fanOut > 1)
                issues.Add(Warn(
                    $"Перекрёсток XOR #{node.Number} имеет {fanOut} исходящих связей. " +
                    $"XOR означает активацию ровно одного исходящего потока.", node.Id));

            // XOR: fan-in тоже должен быть 1
            if (node.Type == "junction-xor" && fanIn > 1)
                issues.Add(Info(
                    $"Перекрёсток XOR #{node.Number} имеет {fanIn} входящих связей. " +
                    $"В семантике XOR одновременно завершается ровно один предшествующий процесс.", node.Id));
        }

        // ─── Проверка UOB-блоков ──────────────────────────────────────

        private static void ValidateUob(
            Node node, List<Link> inLinks, List<Link> outLinks,
            int totalUobs, List<ValidationIssue> issues)
        {
            // Стандартное (нередактированное) название
            if (string.IsNullOrWhiteSpace(node.Text) ||
                node.Text == "Новый блок" || node.Text == "Блок поведения")
                issues.Add(Info(
                    $"UOB-блок #{node.Number} имеет стандартное название. " +
                    $"Задайте осмысленное имя (глагольный оборот: «Проверить», «Обработать»).", node.Id));

            if (totalUobs > 1)
            {
                // Несколько потоков входят в UOB напрямую — нарушение нотации:
                // слияние нескольких потоков должно выполняться через перекрёсток (fan-in)
                if (inLinks.Count >= 2)
                    issues.Add(Err(
                        $"UOB-блок #{node.Number} «{node.Text}» имеет {inLinks.Count} входящих связи(ей) напрямую. " +
                        $"Несколько входящих потоков должны объединяться через перекрёсток (fan-in) перед входом в UOB-блок.", node.Id));

                // Несколько потоков исходят из UOB напрямую — нарушение нотации:
                // ветвление нескольких потоков должно выполняться через перекрёсток (fan-out)
                if (outLinks.Count >= 2)
                    issues.Add(Err(
                        $"UOB-блок #{node.Number} «{node.Text}» имеет {outLinks.Count} исходящих связи(ей) напрямую. " +
                        $"Несколько исходящих потоков должны разветвляться через перекрёсток (fan-out) из UOB-блока.", node.Id));
            }
        }

        // ─── Вспомогательные методы ───────────────────────────────────

        // Возвращает читаемое название типа перекрёстка.
        public static string JunctionName(string type)
        {
            switch (type)
            {
                case "junction-and":      return "AND (асинхр.)";
                case "junction-and-sync": return "AND (синхр.)";
                case "junction-or":       return "OR (асинхр.)";
                case "junction-or-sync":  return "OR (синхр.)";
                case "junction-xor":      return "XOR";
                default:                  return type;
            }
        }

        private static ValidationIssue Err (string m, string id = null)
            => new ValidationIssue { Severity = ValidationSeverity.Error,   Message = m, NodeId = id };
        private static ValidationIssue Warn(string m, string id = null)
            => new ValidationIssue { Severity = ValidationSeverity.Warning, Message = m, NodeId = id };
        private static ValidationIssue Info(string m, string id = null)
            => new ValidationIssue { Severity = ValidationSeverity.Info,    Message = m, NodeId = id };

        // BFS — находит все блоки, достижимые из первого блока списка.
        // Используется для проверки связности диаграммы.
        private static HashSet<string> Reachable(List<Node> nodes, List<Link> links)
        {
            var visited = new HashSet<string>();
            var queue   = new Queue<string>();
            queue.Enqueue(nodes[0].Id);
            while (queue.Count > 0)
            {
                string cur = queue.Dequeue();
                if (!visited.Add(cur)) continue;
                foreach (var l in links)
                {
                    if (l.SourceId == cur && !visited.Contains(l.TargetId)) queue.Enqueue(l.TargetId);
                    if (l.TargetId == cur && !visited.Contains(l.SourceId)) queue.Enqueue(l.SourceId);
                }
            }
            return visited;
        }
    }
}
