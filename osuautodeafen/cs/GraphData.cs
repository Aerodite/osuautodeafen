﻿using System.Collections.Generic;

namespace osuautodeafen.cs
{
    public class GraphData
    {
        public List<Series> Series { get; set; }
        public List<int> XAxis { get; set; }
    }

    public class Series
    {
        public string Name { get; set; }
        public List<double> Data { get; set; }
    }
}