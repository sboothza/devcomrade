﻿// Copyright (C) 2020 by Postprintum Pty Ltd (https://www.postprintum.com),
// which licenses this file to you under Apache License 2.0,
// see the LICENSE file in the project root for more information. 
// Author: Andrew Nosenko (@noseratio)

#nullable enable

using System.Collections.Generic;
using System.Diagnostics;

namespace Tests
{
    public class CategoryTraceListener : TraceListener
    {
        private readonly string _categoty;
        private readonly List<string> _list = new();
        private readonly object _lock = new();

        public CategoryTraceListener(string categoty)
        {
            _categoty = categoty;
        }

        public override bool IsThreadSafe => true;

        public override void Write(string? message) {}

        public override void WriteLine(string? message) {}

        public override void WriteLine(string? message, string? category)
        {
            lock (_lock)
            {
                if (string.CompareOrdinal(category, _categoty) == 0)
                    _list.Add(message ?? string.Empty);
            }
        }

        public override void Write(string? message, string? category)
        {
            WriteLine(message, category);
        }

        public string[] ToArray()
        {
            lock (_lock)
            {
                return _list.ToArray();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _list.Clear();
            }
        }
    }
}