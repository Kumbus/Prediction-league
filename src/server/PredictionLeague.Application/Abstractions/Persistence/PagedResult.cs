namespace PredictionLeague.Application.Abstractions.Persistence;

// Generic paged-list shape used by admin list endpoints (UI renders pagination from Total + PageSize).
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
