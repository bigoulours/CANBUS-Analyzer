﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssociatedDBC
{
    public class DBCwAssociatedBus
    {
        public string Path { get; set; }
        public string Bus { get; set; }
        //public bool isJ1939 { get; set; }
    }

    public static class PlotUtil
    {
        public static TSource MinBy<TSource, TValue>(
            this IEnumerable<TSource> source, Func<TSource, TValue> selector)
        {
            using (var iter = source.GetEnumerator())
            {
                if (!iter.MoveNext()) throw new InvalidOperationException("no data");
                var comparer = Comparer<TValue>.Default;
                var minItem = iter.Current;
                var minValue = selector(minItem);
                while (iter.MoveNext())
                {
                    var item = iter.Current;
                    var value = selector(item);
                    if (comparer.Compare(minValue, value) > 0)
                    {
                        minItem = item;
                        minValue = value;
                    }
                }
                return minItem;
            }
        }

        public static TSource MaxBy<TSource, TValue>(
            this IEnumerable<TSource> source, Func<TSource, TValue> selector)
        {
            using (var iter = source.GetEnumerator())
            {
                if (!iter.MoveNext()) throw new InvalidOperationException("no data");
                var comparer = Comparer<TValue>.Default;
                var maxItem = iter.Current;
                var maxValue = selector(maxItem);
                while (iter.MoveNext())
                {
                    var item = iter.Current;
                    var value = selector(item);
                    if (comparer.Compare(maxValue, value) < 0)
                    {
                        maxItem = item;
                        maxValue = value;
                    }
                }
                return maxItem;
            }
        }
    }
}
