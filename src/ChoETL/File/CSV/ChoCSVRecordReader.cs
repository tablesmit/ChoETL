﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChoETL
{
    internal class ChoCSVRecordReader : ChoRecordReader
    {
        private IChoReaderRecord _callbackRecord;
        private bool _headerFound = false;
        private bool _excelSeparatorFound = false;
        private string[] _fieldNames = new string[] { };
        private bool _configCheckDone = false;

        public ChoCSVRecordConfiguration Configuration
        {
            get;
            private set;
        }

        public ChoCSVRecordReader(Type recordType, ChoCSVRecordConfiguration configuration = null) : base(recordType)
        {
            ChoGuard.ArgumentNotNull(configuration, "Configuration");
            Configuration = configuration;

            _callbackRecord = ChoSurrogateObjectCache.CreateSurrogateObject<IChoReaderRecord>(recordType);

            Configuration.Validate();
        }

        public override void LoadSchema(object source)
        {
            var e = AsEnumerable(source, new TraceSwitch("ChoETLSwitch", "ChoETL Trace Switch", "Off")).GetEnumerator();
            e.MoveNext();
        }

        public override IEnumerable<object> AsEnumerable(object source, Func<object, bool?> filterFunc = null)
        {
            return AsEnumerable(source, ChoETLFramework.TraceSwitch, filterFunc);
        }

        private IEnumerable<object> AsEnumerable(object source, TraceSwitch traceSwitch, Func<object, bool?> filterFunc = null)
        {
            TraceSwitch = traceSwitch;

            StreamReader sr = source as StreamReader;
            ChoGuard.ArgumentNotNull(sr, "StreamReader");

            sr.Seek(0, SeekOrigin.Begin);

            if (!RaiseBeginLoad(sr))
                yield break;

            string[] commentTokens = Configuration.Comments;

            using (ChoPeekEnumerator<Tuple<int, string>> e = new ChoPeekEnumerator<Tuple<int, string>>(
                new ChoIndexedEnumerator<string>(sr.ReadLines(Configuration.EOLDelimiter, Configuration.QuoteChar)).ToEnumerable(),
                (pair) =>
                {
                    //bool isStateAvail = IsStateAvail();

                    bool? skip = false;

                    //if (isStateAvail)
                    //{
                    //    if (!IsStateMatches(item))
                    //    {
                    //        skip = filterFunc != null ? filterFunc(item) : false;
                    //    }
                    //    else
                    //        skip = true;
                    //}
                    //else
                    //    skip = filterFunc != null ? filterFunc(item) : false;

                    if (skip == null)
                        return null;

                    //if (!(sr.BaseStream is MemoryStream))
                    //{
                        ChoETLFramework.WriteLog(TraceSwitch.TraceVerbose, Environment.NewLine);

                        if (!skip.Value)
                            ChoETLFramework.WriteLog(TraceSwitch.TraceVerbose, "Loading line [{0}]...".FormatString(pair.Item1));
                        else
                            ChoETLFramework.WriteLog(TraceSwitch.TraceVerbose, "Skipping line [{0}]...".FormatString(pair.Item1));
                    //}

                    if (skip.Value)
                        return skip;

                    //if (!(sr.BaseStream is MemoryStream))
                    //    ChoETLFramework.WriteLog(TraceSwitch.TraceVerbose, ChoETLFramework.Switch.TraceVerbose, "Loading line [{0}]...".FormatString(item.Item1));

                    //if (Task != null)
                    //    return !IsStateNOTExistsOrNOTMatch(item);

                    if (pair.Item2.IsNullOrWhiteSpace())
                    {
                        if (!Configuration.IgnoreEmptyLine)
                            throw new ChoParserException("Empty line found at {0} location.".FormatString(e.Peek.Item1));
                        else
                        {
                            ChoETLFramework.WriteLog(TraceSwitch.TraceVerbose, "Empty line found at [{0}]...".FormatString(pair.Item1));
                            return true;
                        }
                    }

                    //LoadExcelSeparator if any
                    if (pair.Item1 == 1
                        && !_excelSeparatorFound)
                    {
                        ChoETLFramework.WriteLog(TraceSwitch.TraceVerbose, "Inspecting for excel separator at [{0}]...".FormatString(pair.Item1));
                        bool retVal = LoadExcelSeperatorIfAny(pair);
                        _excelSeparatorFound = true;

                        if (Configuration.HasExcelSeparator.HasValue
                            && Configuration.HasExcelSeparator.Value
                            && !retVal)
                            throw new ChoParserException("Missing excel separator header line in the file.");

                        if (retVal)
                            return true;
                    }

                    if (commentTokens == null)
                        return false;
                    else
                    {
                        var x = (from comment in commentTokens
                                 where !pair.Item2.IsNull() && pair.Item2.StartsWith(comment, true, Configuration.Culture)
                                 select comment).FirstOrDefault();
                        if (x != null)
                        {
                            ChoETLFramework.WriteLog(TraceSwitch.TraceVerbose, "Comment line found at [{0}]...".FormatString(pair.Item1));
                            return true;
                        }
                    }

                    if (!_configCheckDone)
                    {
                        Configuration.Validate(GetHeaders(pair.Item2));
                        _configCheckDone = true;
                    }

                    //LoadHeader if any
                    if (Configuration.CSVFileHeaderConfiguration.HasHeaderRecord
                        && !_headerFound)
                    {
                        ChoETLFramework.WriteLog(TraceSwitch.TraceVerbose, "Loading header line at [{0}]...".FormatString(pair.Item1));
                        LoadHeaderLine(pair);
                        _headerFound = true;
                        return true;
                    }

                    return false;
                }))
            {
                while (true)
                {
                    Tuple<int, string> pair = e.Peek;
                    if (pair == null)
                    {
                        RaiseEndLoad(sr);
                        yield break;
                    }

                    object rec = ChoActivator.CreateInstance(RecordType);
                    if (!LoadLine(pair, ref rec))
                        yield break;

                    //StoreState(e.Current, rec != null);

                    e.MoveNext();

                    if (rec == null)
                        continue;

                    yield return rec;
                }
            }
        }

        private bool LoadLine(Tuple<int, string> pair, ref object rec)
        {
            try
            {
                if (!RaiseBeforeRecordLoad(rec, ref pair))
                    return false;

                if (pair == null || pair.Item2 == null)
                    return false;

                if (!pair.Item2.IsNullOrWhiteSpace())
                {
                    if (!FillRecord(rec, pair))
                        return false;

                    if (!(rec is ExpandoObject) 
                        && (Configuration.ObjectValidationMode & ChoObjectValidationMode.ObjectLevel) == ChoObjectValidationMode.ObjectLevel)
                        ChoValidator.Validate(rec);
                }

                if (!RaiseAfterRecordLoad(rec, pair))
                    return false;
            }
            catch (ChoParserException)
            {
                throw;
            }
            catch (ChoMissingRecordFieldException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ChoETLFramework.HandleException(ex);
                if (Configuration.ErrorMode == ChoErrorMode.IgnoreAndContinue)
                {
                    rec = null;
                }
                else if (Configuration.ErrorMode == ChoErrorMode.ReportAndContinue)
                {
                    if (!RaiseRecordLoadError(rec, pair, ex))
                        throw;
                }
                else
                    throw;

                return true;
            }

            return true;
        }

        private Dictionary<string, string> ToFieldNameValues(string[] fieldValues)
        {
            int index = 1;
            Dictionary<string, string> fnv = new Dictionary<string, string>(Configuration.CSVFileHeaderConfiguration.StringComparer);
            if (Configuration.CSVFileHeaderConfiguration.HasHeaderRecord)
            {
                foreach (var name in _fieldNames)
                {
                    if (index - 1 < fieldValues.Length)
                        fnv.Add(name, fieldValues[index - 1]);
                    else
                        fnv.Add(name, String.Empty);

                    index++;
                }
            }
            return fnv;
        }

        private bool FillRecord(object rec, Tuple<int, string> pair)
        {
            int lineNo;
            string line;

            lineNo = pair.Item1;
            line = pair.Item2;

            object fieldValue = null;

            string[] fieldValues = (from x in line.Split(Configuration.Delimiter, Configuration.StringSplitOptions, Configuration.QuoteChar)
                               select x).ToArray();
            if (Configuration.ColumnCountStrict)
            {
                if (fieldValues.Length != Configuration.RecordFieldConfigurations.Count)
                    throw new ChoParserException("Incorrect number of field values found at line [{2}]. Expected [{0}] field values. Found [{1}] field values.".FormatString(Configuration.RecordFieldConfigurations.Count, fieldValues.Length, pair.Item1));
            }

            Dictionary<string, string> _fieldNameValues = ToFieldNameValues(fieldValues);

            ValidateLine(pair.Item1, fieldValues);

            ChoCSVRecordFieldConfiguration fieldConfig = null;
            foreach (KeyValuePair<string, ChoCSVRecordFieldConfiguration> kvp in Configuration.RecordFieldConfigurationsDict)
            {
                fieldConfig = kvp.Value;

                if (Configuration.CSVFileHeaderConfiguration.HasHeaderRecord)
                {
                    if (_fieldNameValues.ContainsKey(fieldConfig.FieldName))
                        fieldValue = _fieldNameValues[fieldConfig.FieldName];
                    else if (Configuration.ColumnCountStrict)
                        throw new ChoParserException("No matching '{0}' field header found.".FormatString(fieldConfig.FieldName));
                }
                else
                {
                    if (fieldConfig.FieldPosition - 1 < fieldValues.Length)
                        fieldValue = fieldValues[fieldConfig.FieldPosition - 1];
                    else if (Configuration.ColumnCountStrict)
                        throw new ChoParserException("Missing field value for {0} [Position: {1}] field.".FormatString(fieldConfig.FieldName, fieldConfig.FieldPosition));
                }

                fieldValue = CleanFieldValue(fieldConfig, fieldValue as string);

                if (!RaiseBeforeRecordFieldLoad(rec, pair.Item1, kvp.Key, ref fieldValue))
                    continue;

                try
                {
                    bool ignoreFieldValue = fieldConfig.IgnoreFieldValue(fieldValue);
                    if (!ignoreFieldValue)
                    {
                        if (rec is ExpandoObject)
                        {
                            if (fieldConfig.FieldType != typeof(string))
                                fieldValue = ChoConvert.ConvertTo(fieldValue, fieldConfig.FieldType, Configuration.Culture);
                            var x = rec as IDictionary<string, Object>;
                            x.Add(kvp.Key, fieldValue);
                        }
                        else
                        {
                            if (ChoType.HasProperty(rec.GetType(), kvp.Key))
                            {
                                ChoType.ConvertNSetMemberValue(rec, kvp.Key, fieldValue);
                                fieldValue = ChoType.GetMemberValue(rec, kvp.Key);

                                if ((Configuration.ObjectValidationMode & ChoObjectValidationMode.MemberLevel) == ChoObjectValidationMode.MemberLevel)
                                    ChoValidator.ValididateFor(rec, kvp.Key);
                            }
                            else
                                throw new ChoMissingRecordFieldException("Missing '{0}' property in {1} type.".FormatString(kvp.Key, ChoType.GetTypeName(rec)));
                        }
                    }

                    if (!RaiseAfterRecordFieldLoad(rec, pair.Item1, kvp.Key, fieldValue))
                        return false;
                }
                catch (ChoParserException)
                {
                    throw;
                }
                catch (ChoMissingRecordFieldException)
                {
                    if (Configuration.ThrowAndStopOnMissingField)
                        throw;
                }
                catch (Exception ex)
                {
                    ChoETLFramework.HandleException(ex);

                    if (fieldConfig.ErrorMode == ChoErrorMode.ThrowAndStop)
                        throw;
                    try
                    {
                        ChoFallbackValueAttribute fbAttr = ChoTypeDescriptor.GetPropetyAttribute<ChoFallbackValueAttribute>(rec.GetType(), kvp.Key);
                        if (fbAttr != null)
                        {
                            if (!fbAttr.Value.IsNullOrDbNull())
                                ChoType.ConvertNSetMemberValue(rec, kvp.Key, fbAttr.Value);
                        }
                        else
                            throw;
                    }
                    catch
                    {
                        if (fieldConfig.ErrorMode == ChoErrorMode.IgnoreAndContinue)
                        {
                            continue;
                        }
                        else if (fieldConfig.ErrorMode == ChoErrorMode.ReportAndContinue)
                        {
                            if (!RaiseRecordFieldLoadError(rec, pair.Item1, kvp.Key, fieldValue, ex))
                                throw;
                        }
                        else
                            throw;
                    }
                }
            }

            return true;
        }

        private string CleanFieldValue(ChoCSVRecordFieldConfiguration config, string fieldValue)
        {
            if (fieldValue.IsNull()) return fieldValue;

            if (fieldValue != null)
            {
                switch (config.FieldValueTrimOption)
                {
                    case ChoFieldValueTrimOption.Trim:
                        fieldValue = fieldValue.Trim();
                        break;
                    case ChoFieldValueTrimOption.TrimStart:
                        fieldValue = fieldValue.TrimStart();
                        break;
                    case ChoFieldValueTrimOption.TrimEnd:
                        fieldValue = fieldValue.TrimEnd();
                        break;
                }
            }

            if (config.Size != null)
            {
                if (fieldValue.Length > config.Size.Value)
                {
                    if (!config.Truncate)
                        throw new ChoParserException("Incorrect field value length found for '{0}' member [Expected: {1}, Actual: {2}].".FormatString(config.FieldName, config.Size.Value, fieldValue.Length));
                    else
                        fieldValue = fieldValue.Substring(0, config.Size.Value);
                }
            }

            if (config.QuoteField != null && config.QuoteField.Value && fieldValue.StartsWith(@"""") && fieldValue.EndsWith(@""""))
                return fieldValue.Substring(1, fieldValue.Length - 2);
            else if ((fieldValue.Contains(Configuration.Delimiter)
                || fieldValue.Contains(Configuration.EOLDelimiter)) && fieldValue.StartsWith(@"""") && fieldValue.EndsWith(@""""))
                return fieldValue.Substring(1, fieldValue.Length - 2);
            else
                return fieldValue;
        }

        private void ValidateLine(int lineNo, string[] fieldValues)
        {
            int maxPos = Configuration.MaxFieldPosition;

            if (Configuration.ColumnCountStrict)
            {
                if (fieldValues.Length != maxPos)
                    throw new ApplicationException("Mismatched number of fields found at {0} line. [Expected: {1}, Found: {2}].".FormatString(
                        lineNo, maxPos, fieldValues.Length));
            }

            //ChoCSVRecordFieldAttribute attr = null;
            //foreach (Tuple<MemberInfo, ChoOrderedAttribute> member in _members)
            //{
            //    if (attr.Position > fields.Length)
            //        throw new ApplicationException("Record Member '{0}' has incorrect Position specified.".FormatString(ChoType.GetMemberName(member.Item1)));
            //}
        }

        private bool LoadExcelSeperatorIfAny(Tuple<int, string> pair)
        {
            string line = pair.Item2.NTrim();
            if (!line.IsNullOrWhiteSpace() && line.StartsWith("sep=", true, Configuration.Culture))
            {
                ChoETLFramework.WriteLog(TraceSwitch.TraceVerbose, "Excel separator specified at [{0}]...".FormatString(pair.Item1));
                string delimiter = line.Substring(4);
                if (!delimiter.IsNullOrWhiteSpace())
                {
                    ChoETLFramework.WriteLog(TraceSwitch.TraceVerbose, "Excel separator [{0}] found.".FormatString(delimiter));
                    Configuration.Delimiter = delimiter;
                }

                return true;
            }

            ChoETLFramework.WriteLog(TraceSwitch.TraceVerbose, "Excel separator NOT found. Default separator [{0}] used.".FormatString(Configuration.Delimiter));
            return false;
        }

        private string[] GetHeaders(string line)
        {
            if (Configuration.CSVFileHeaderConfiguration.HasHeaderRecord)
                return (from x in line.Split(Configuration.Delimiter, Configuration.StringSplitOptions, Configuration.QuoteChar)
                        select CleanHeaderValue(x)).ToArray();
            else
            {
                int index = 0;
                return (from x in line.Split(Configuration.Delimiter, Configuration.StringSplitOptions, Configuration.QuoteChar)
                        select "Column{0}".FormatString(++index)).ToArray();
            }
        }

        private void LoadHeaderLine(Tuple<int, string> pair)
        {
            string line = pair.Item2;

            //Validate header
            _fieldNames = GetHeaders(line);
            
            if (_fieldNames.Length == 0)
                throw new ChoParserException("No headers found.");

            //Check any header value empty
            if (_fieldNames.Where(i => i.IsNullOrWhiteSpace()).Any())
                throw new ChoParserException("At least one of the field header is empty.");

            if (Configuration.ColumnCountStrict)
            {
                if (_fieldNames.Length != Configuration.RecordFieldConfigurations.Count)
                    throw new ChoParserException("Incorrect number of field headers found. Expected [{0}] fields. Found [{1}] fields.".FormatString(Configuration.RecordFieldConfigurations.Count, _fieldNames.Length));

                string[] foundList = Configuration.RecordFieldConfigurations.Select(i => i.FieldName).Except(_fieldNames, Configuration.CSVFileHeaderConfiguration.StringComparer).ToArray();
                if (foundList.Any())
                    throw new ChoParserException("Header names [{0}] specified in configuration/entity are not found in file header.".FormatString(String.Join(",", foundList)));
            }
        }

        private string CleanHeaderValue(string fieldValue)
        {
            if (fieldValue.IsNull()) return fieldValue;

            ChoFileHeaderConfiguration config = Configuration.CSVFileHeaderConfiguration;
            if (fieldValue != null)
            {
                switch (config.TrimOption)
                {
                    case ChoFieldValueTrimOption.Trim:
                        fieldValue = fieldValue.Trim();
                        break;
                    case ChoFieldValueTrimOption.TrimStart:
                        fieldValue = fieldValue.TrimStart();
                        break;
                    case ChoFieldValueTrimOption.TrimEnd:
                        fieldValue = fieldValue.TrimEnd();
                        break;
                }
            }

            if (Configuration.QuoteAllFields && fieldValue.StartsWith(@"""") && fieldValue.EndsWith(@""""))
                return fieldValue.Substring(1, fieldValue.Length - 2);
            else
                return fieldValue;
        }

        private bool RaiseBeginLoad(object state)
        {
            if (_callbackRecord == null) return true;
            return ChoFuncEx.RunWithIgnoreError(() => _callbackRecord.BeginLoad(state), true);
        }

        private void RaiseEndLoad(object state)
        {
            if (_callbackRecord == null) return;
            ChoActionEx.RunWithIgnoreError(() => _callbackRecord.EndLoad(state));
        }

        private bool RaiseBeforeRecordLoad(object target, ref Tuple<int, string> pair)
        {
            if (_callbackRecord == null) return true;
            int index = pair.Item1;
            object state = pair.Item2;
            bool retValue = ChoFuncEx.RunWithIgnoreError(() => _callbackRecord.BeforeRecordLoad(target, index, ref state), true);

            if (retValue)
                pair = new Tuple<int, string>(index, state as string);

            return retValue;
        }

        private bool RaiseAfterRecordLoad(object target, Tuple<int, string> pair)
        {
            if (_callbackRecord == null) return true;
            return ChoFuncEx.RunWithIgnoreError(() => _callbackRecord.AfterRecordLoad(target, pair.Item1, pair.Item2), true);
        }

        private bool RaiseRecordLoadError(object target, Tuple<int, string> pair, Exception ex)
        {
            if (_callbackRecord == null) return true;
            return ChoFuncEx.RunWithIgnoreError(() => _callbackRecord.RecordLoadError(target, pair.Item1, pair.Item2, ex), false);
        }

        private bool RaiseBeforeRecordFieldLoad(object target, int index, string propName, ref object value)
        {
            if (_callbackRecord == null) return true;
            object state = value;
            bool retValue = ChoFuncEx.RunWithIgnoreError(() => _callbackRecord.BeforeRecordFieldLoad(target, index, propName, ref state), true);

            if (retValue)
                value = state;

            return retValue;
        }

        private bool RaiseAfterRecordFieldLoad(object target, int index, string propName, object value)
        {
            if (_callbackRecord == null) return true;
            return ChoFuncEx.RunWithIgnoreError(() => _callbackRecord.AfterRecordFieldLoad(target, index, propName, value), true);
        }

        private bool RaiseRecordFieldLoadError(object target, int index, string propName, object value, Exception ex)
        {
            if (_callbackRecord == null) return true;
            return ChoFuncEx.RunWithIgnoreError(() => _callbackRecord.RecordFieldLoadError(target, index, propName, value, ex), false);
        }
    }
}
