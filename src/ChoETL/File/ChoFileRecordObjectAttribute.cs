﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChoETL
{
    public abstract class ChoFileRecordObjectAttribute : ChoRecordObjectAttribute
    {
        public string EOLDelimiter
        {
            get;
            set;
        }
        public CultureInfo Culture
        {
            get;
            set;
        }
        public bool IgnoreEmptyLine
        {
            get;
            set;
        }
        public string Comments
        {
            get;
            set;
        }
        public char QuoteChar
        {
            get;
            set;
        }
        public bool QuoteAllFields
        {
            get;
            set;
        }
        public ChoStringSplitOptions StringSplitOptions
        {
            get;
            set;
        }
        public string Encoding
        {
            get;
            set;
        }
        internal bool? _hasExcelSeparator;
        public bool HasExcelSeparator
        {
            get { throw new NotSupportedException(); }
            set { _hasExcelSeparator = value; }
        }
        public bool ColumnCountStrict
        {
            get;
            set;
        }
        public int BufferSize
        {
            get;
            set;
        }

        public ChoFileRecordObjectAttribute()
        {
            EOLDelimiter = Environment.NewLine;
            BufferSize = 2048;
            Comments = "#, //";
            Culture = CultureInfo.CurrentCulture;
            EOLDelimiter = Environment.NewLine;
            IgnoreEmptyLine = false;
            ColumnCountStrict = true;
            QuoteChar = '"';
            QuoteAllFields = false;
            StringSplitOptions = ChoStringSplitOptions.None;
            Encoding = "UTF8";
        }
    }
}
