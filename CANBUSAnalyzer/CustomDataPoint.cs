﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OxyPlot;

namespace CANBUS
{
    public class CustomDataPoint : IDataPointProvider
    {
        public double X { get; set; }
        public double Y { get; set; }
        public string Description { get; set; }
        public DataPoint GetDataPoint() => new DataPoint(X, Y);

        public CustomDataPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }
}
