using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuizMester
{
    public class ScoreboardEntry
    {
        public int Rank { get; set; }
        public string PlayerName { get; set; }
        public int Score { get; set; }
        public string CompletedCategories { get; set; }  // optional, if you track it
        public DateTime? Date { get; set; }
    }
}
