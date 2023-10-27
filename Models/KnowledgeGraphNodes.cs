namespace Sciencetopia.Models
{
    public class Node
    {
        public int Identity { get; set; }
        public List<string>? Labels { get; set; }
        public NodeProperties? Properties { get; set; }
        public string? ElementId { get; set; }
    }

    public class NodeProperties
    {
        public string? Link { get; set; }
        public string? Name { get; set; }
    }

    public class Relationship
    {
        public int Identity { get; set; }
        public int Start { get; set; }
        public int End { get; set; }
        public string? Type { get; set; }
        public string? ElementId { get; set; }
        public string? StartNodeElementId { get; set; }
        public string? EndNodeElementId { get; set; }
    }
}