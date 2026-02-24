using System.Collections.Generic;

namespace QueryToExcell.Models
{
    public class QueryInfo
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string SqlText { get; set; }
        // Lista dei parametri richiesti da questa query
        public List<QueryParameter> Parameters { get; set; } = new List<QueryParameter>();
    }
}