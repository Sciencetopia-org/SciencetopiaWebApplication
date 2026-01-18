namespace Sciencetopia.DTOs;

public record ResourcesStatusRequest(IEnumerable<Guid> resourceIds);
public record ResourceCompletedStatusDto(Guid resourceId, bool completed);

