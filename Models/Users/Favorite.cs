namespace Sciencetopia.Models
{
    public class Favorite
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid GroupId { get; set; }
        public string? Name { get; set; }
        public required string Type { get; set; }
        public DateTime CreatedAt { get; set; }

        public GroupEntity? Group { get; set; }
    }
}
