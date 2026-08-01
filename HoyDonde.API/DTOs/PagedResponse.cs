using System.Collections.Generic;

namespace HoyDonde.API.DTOs
{
    public class PagedResponse<T>
    {
        public IEnumerable<T> Data { get; set; } = new List<T>();
        public string? LastDocumentId { get; set; }
        public bool HasNextPage { get; set; }
    }
}
