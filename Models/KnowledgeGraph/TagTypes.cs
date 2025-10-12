using System.ComponentModel.DataAnnotations;

public class TagTypes
{
    [Key]
    public Guid TagStableId { get; set; }
    public int TypeId { get; set; }
}
