using System;
using System.Collections.Generic;
using System.Text;

namespace QueryToExcell.Models
{
    public class QueryParameter
    {
        public int Id { get; set; }
        public string Name { get; set; } // es: "@DataInizio"
        public string Type { get; set; } // es: "date", "number", "text"
        public string Label { get; set; } // es: "Seleziona la data di inizio"
    }
}