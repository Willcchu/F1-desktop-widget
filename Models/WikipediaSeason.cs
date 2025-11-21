using System;
using System.Collections.Generic;
using System.Text;

namespace F1_widgets.Models
{
    public class WikipediaSeason
    {
        public string season { get; set; } = "";
        public List<WikipediaRound> rounds { get; set; } = new();
    }

    public class WikipediaRound
    {
        public int round { get; set; }
        public string name { get; set; } = "";
        public string circuit { get; set; } = "";
        public string country { get; set; } = "";
        public string date { get; set; } = "";

        public WikipediaSessions sessions { get; set; } = new();
    }

    public class WikipediaSessions
    {
        public string fp1 { get; set; } = "";
        public string fp2 { get; set; } = "";
        public string fp3 { get; set; } = "";
        public string qualifying { get; set; } = "";
        public string sprint { get; set; } = "";
        public string race { get; set; } = "";
    }
}

