﻿// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Collections;
using Framework.Constants;
using Framework.Dynamic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Game.DataStorage
{
    class DBReader
    {
        private const uint WDC4FmtSig = 0x34434457; // WDC4

        public WDCHeader Header;
        public FieldMetaData[] FieldMeta;
        public ColumnMetaData[] ColumnMeta;
        public Value32[][] PalletData;
        public Dictionary<int, Value32>[] CommonData;
        Dictionary<int, int[]> _encryptedIDs;

        public Dictionary<int, WDC4Row> Records = new();

        public bool Load(Stream stream)
        {
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                Header = new WDCHeader();
                Header.Signature = reader.ReadUInt32();
                if (Header.Signature != WDC4FmtSig)
                    return false;

                Header.RecordCount = reader.ReadInt32();
                Header.FieldCount = reader.ReadInt32();
                Header.RecordSize = reader.ReadInt32();
                Header.StringTableSize = reader.ReadInt32();
                Header.TableHash = reader.ReadUInt32();
                Header.LayoutHash = reader.ReadUInt32();
                Header.MinId = reader.ReadInt32();
                Header.MaxId = reader.ReadInt32();
                Header.Locale = reader.ReadInt32();
                Header.Flags = (HeaderFlags)reader.ReadUInt16();
                Header.IdIndex = reader.ReadUInt16();
                Header.TotalFieldCount = reader.ReadInt32();
                Header.BitpackedDataOffset = reader.ReadInt32();
                Header.LookupColumnCount = reader.ReadInt32();
                Header.ColumnMetaSize = reader.ReadInt32();
                Header.CommonDataSize = reader.ReadInt32();
                Header.PalletDataSize = reader.ReadInt32();
                Header.SectionsCount = reader.ReadInt32();

                var sections = reader.ReadArray<SectionHeader>(Header.SectionsCount);

                // field meta data
                FieldMeta = reader.ReadArray<FieldMetaData>(Header.FieldCount);

                // column meta data 
                ColumnMeta = reader.ReadArray<ColumnMetaData>(Header.FieldCount);

                // pallet data
                PalletData = new Value32[ColumnMeta.Length][];
                for (int i = 0; i < ColumnMeta.Length; i++)
                {
                    if (ColumnMeta[i].CompressionType == DB2ColumnCompression.Pallet || ColumnMeta[i].CompressionType == DB2ColumnCompression.PalletArray)
                    {
                        PalletData[i] = reader.ReadArray<Value32>(ColumnMeta[i].AdditionalDataSize / 4);
                    }
                }

                // common data
                CommonData = new Dictionary<int, Value32>[ColumnMeta.Length];
                for (int i = 0; i < ColumnMeta.Length; i++)
                {
                    if (ColumnMeta[i].CompressionType == DB2ColumnCompression.Common)
                    {
                        Dictionary<int, Value32> commonValues = new();
                        CommonData[i] = commonValues;

                        for (int j = 0; j < ColumnMeta[i].AdditionalDataSize / 8; j++)
                            commonValues[reader.ReadInt32()] = reader.Read<Value32>();
                    }
                }

                // encrypted IDs
                _encryptedIDs = new();
                for (int i = 1; i < Header.SectionsCount; i++)
                {
                    var encryptedIDCount = reader.ReadInt32();

                    // If tactkey in section header is 0'd out, skip these IDs
                    if (sections[i].TactKeyLookup == 0 || sections[i].TactKeyLookup == 0x5452494E49545900)
                        reader.BaseStream.Position += encryptedIDCount * 4;
                    else
                        _encryptedIDs.Add(i, reader.ReadArray<int>(encryptedIDCount));
                }

                long previousRecordCount = 0;
                foreach (var section in sections)
                {
                    reader.BaseStream.Position = section.FileOffset;

                    byte[] recordsData;
                    Dictionary<long, string> stringsTable = null;
                    SparseEntry[] sparseEntries = null;

                    if (!Header.HasOffsetTable())
                    {
                        // records data
                        recordsData = reader.ReadBytes(section.NumRecords * Header.RecordSize);

                        // string data
                        stringsTable = new Dictionary<long, string>();

                        for (int i = 0; i < section.StringTableSize;)
                        {
                            long oldPos = reader.BaseStream.Position;

                            stringsTable[i] = reader.ReadCString();

                            i += (int)(reader.BaseStream.Position - oldPos);
                        }
                    }
                    else
                    {
                        // sparse data with inlined strings
                        recordsData = reader.ReadBytes(section.OffsetRecordsEndOffset - section.FileOffset);

                        if (reader.BaseStream.Position != section.OffsetRecordsEndOffset)
                            throw new Exception("reader.BaseStream.Position != sections[sectionIndex].SparseTableOffset");
                    }

                    // skip encrypted sections => has tact key + record data is zero filled
                    if (section.TactKeyLookup != 0 && Array.TrueForAll(recordsData, x => x == 0))
                    {
                        bool completelyZero = false;
                        if (section.IndexDataSize > 0 || section.CopyTableCount > 0)
                        {
                            // this will be the record id from m_indexData or m_copyData
                            // if this is zero then the id for this record will be zero which is invalid
                            completelyZero = reader.ReadInt32() == 0;
                            reader.BaseStream.Position -= 4;
                        }
                        else if (section.OffsetMapIDCount > 0)
                        {
                            // this will be the first m_sparseEntries entry
                            // confirm it's size is not zero otherwise it is invalid
                            completelyZero = reader.Read<SparseEntry>().Size == 0;
                            reader.BaseStream.Position -= 6;
                        }
                        else
                        {
                            // there is no additional data and recordsData is already known to be zeroed
                            // therefore the record will have an id of zero which is invalid
                            completelyZero = true;
                        }

                        if (completelyZero)
                        {
                            previousRecordCount += section.NumRecords;
                            continue;
                        }
                    }

                    Array.Resize(ref recordsData, recordsData.Length + 8); // pad with extra zeros so we don't crash when reading

                    // index data
                    int[] indexData = reader.ReadArray<int>(section.IndexDataSize / 4);
                    bool isIndexEmpty = Header.HasIndexTable() && indexData.Count(i => i == 0) == section.NumRecords;

                    // duplicate rows data
                    Dictionary<int, int> copyData = new();

                    for (int i = 0; i < section.CopyTableCount; i++)
                        copyData[reader.ReadInt32()] = reader.ReadInt32();

                    if (section.OffsetMapIDCount > 0)
                        sparseEntries = reader.ReadArray<SparseEntry>(section.OffsetMapIDCount);

                    // reference data
                    ReferenceData refData = null;

                    if (section.ParentLookupDataSize > 0)
                    {
                        refData = new ReferenceData
                        {
                            NumRecords = reader.ReadInt32(),
                            MinId = reader.ReadInt32(),
                            MaxId = reader.ReadInt32()
                        };

                        refData.Entries = new Dictionary<int, int>();
                        ReferenceEntry[] entries = reader.ReadArray<ReferenceEntry>(refData.NumRecords);
                        foreach (var entry in entries)
                            refData.Entries[entry.Index] = entry.Id;
                    }
                    else
                    {
                        refData = new ReferenceData
                        {
                            Entries = new Dictionary<int, int>()
                        };
                    }

                    if (section.OffsetMapIDCount > 0)
                    {
                        int[] sparseIndexData = reader.ReadArray<int>(section.OffsetMapIDCount);

                        if (Header.HasIndexTable() && indexData.Length != sparseIndexData.Length)
                            throw new Exception("indexData.Length != sparseIndexData.Length");

                        indexData = sparseIndexData;
                    }

                    BitReader bitReader = new(recordsData);

                    for (int i = 0; i < section.NumRecords; ++i)
                    {
                        bitReader.Position = 0;
                        if (Header.HasOffsetTable())
                            bitReader.Offset = sparseEntries[i].Offset - section.FileOffset;
                        else
                            bitReader.Offset = i * Header.RecordSize;

                        bool hasRef = refData.Entries.TryGetValue(i, out int refId);

                        long recordIndex = i + previousRecordCount;
                        long recordOffset =  (recordIndex * Header.RecordSize) - (Header.RecordCount * Header.RecordSize);

                        var rec = new WDC4Row(this, bitReader, (int)recordOffset, Header.HasIndexTable() ? (isIndexEmpty ? i : indexData[i]) : -1, hasRef ? refId : -1, stringsTable);
                        Records.Add(rec.Id, rec);
                    }

                    foreach (var copyRow in copyData)
                    {
                        if (copyRow.Key != 0)
                        {
                            var rec = Records[copyRow.Value].Clone();
                            rec.Id = copyRow.Key;
                            Records.Add(copyRow.Key, rec);
                        }
                    }

                    previousRecordCount += section.NumRecords;
                }
            }

            return true;            
        }
    }

    class WDC4Row
    {
        private BitReader _data;
        private int _dataOffset;
        private int _recordsOffset;
        private int _refId;
        private bool _dataHasId;

        public int Id { get; set; }

        private FieldMetaData[] _fieldMeta;
        private ColumnMetaData[] _columnMeta;
        private Value32[][] _palletData;
        private Dictionary<int, Value32>[] _commonData;
        private Dictionary<long, string> _stringsTable;

        public WDC4Row(DBReader reader, BitReader data, int recordsOffset, int id, int refId, Dictionary<long, string> stringsTable)
        {
            _data = data;
            _recordsOffset = recordsOffset;
            _refId = refId;

            _dataOffset = _data.Offset;

            _fieldMeta = reader.FieldMeta;
            _columnMeta = reader.ColumnMeta.ToArray();
            _palletData = reader.PalletData;
            _commonData = reader.CommonData;
            _stringsTable = stringsTable;

            if (id != -1)
                Id = id;
            else
            {
                int idFieldIndex = reader.Header.IdIndex;
                _data.Position = _columnMeta[idFieldIndex].RecordOffset;

                Id = GetFieldValue<int>(idFieldIndex);
                _dataHasId = true;
            }
        }

        T GetFieldValue<T>(int fieldIndex) where T : unmanaged
        {
            var columnMeta = _columnMeta[fieldIndex];
            switch (columnMeta.CompressionType)
            {
                case DB2ColumnCompression.None:
                    int bitSize = 32 - _fieldMeta[fieldIndex].Bits;
                    if (bitSize > 0)
                        return _data.Read<T>(bitSize);
                    else
                        return _data.Read<T>(columnMeta.Immediate.BitWidth);
                case DB2ColumnCompression.Immediate:
                    return _data.Read<T>(columnMeta.Immediate.BitWidth);
                case DB2ColumnCompression.SignedImmediate:
                    return _data.ReadSigned<T>(columnMeta.Immediate.BitWidth);
                case DB2ColumnCompression.Common:
                    if (_commonData[fieldIndex].TryGetValue(Id, out Value32 val))
                        return val.As<T>();
                    else
                        return columnMeta.Common.DefaultValue.As<T>();
                case DB2ColumnCompression.Pallet:
                case DB2ColumnCompression.PalletArray:
                    int palletIndex = _data.Read<int>(columnMeta.Pallet.BitWidth);
                    var result1 = _palletData[fieldIndex][palletIndex];
                    var result2 = result1.As<T>();
                    return result2;
            }
            throw new Exception($"Unexpected compression Type {_columnMeta[fieldIndex].CompressionType}");
        }

        T[] GetFieldValueArray<T>(int fieldIndex, int arraySize) where T : unmanaged
        {
            var columnMeta = _columnMeta[fieldIndex];

            switch (columnMeta.CompressionType)
            {
                case DB2ColumnCompression.None:
                    int bitSize = 32 - _fieldMeta[fieldIndex].Bits;

                    T[] arr1 = new T[arraySize];

                    for (int i = 0; i < arr1.Length; i++)
                    {
                        if (bitSize > 0)
                            arr1[i] = _data.Read<T>(bitSize);
                        else
                            arr1[i] = _data.Read<T>(columnMeta.Immediate.BitWidth);
                    }

                    return arr1;
                case DB2ColumnCompression.Immediate:
                    T[] arr2 = new T[arraySize];

                    for (int i = 0; i < arr2.Length; i++)
                        arr2[i] = _data.Read<T>(columnMeta.Immediate.BitWidth);

                    return arr2;
                case DB2ColumnCompression.SignedImmediate:
                    T[] arr3 = new T[arraySize];

                    for (int i = 0; i < arr3.Length; i++)
                        arr3[i] = _data.ReadSigned<T>(columnMeta.Immediate.BitWidth);

                    return arr3;
                case DB2ColumnCompression.PalletArray:
                    int cardinality = columnMeta.Pallet.Cardinality;

                    if (arraySize != cardinality)
                        throw new Exception("Struct missmatch for pallet array field?");

                    int palletArrayIndex = _data.Read<int>(columnMeta.Pallet.BitWidth);

                    T[] arr4 = new T[cardinality];

                    for (int i = 0; i < arr4.Length; i++)
                        arr4[i] = _palletData[fieldIndex][i + cardinality * palletArrayIndex].As<T>();

                    return arr4;
            }
            throw new Exception(string.Format("Unexpected compression Type {0}", columnMeta.CompressionType));
        }

        public T As<T>() where T : new()
        {
            _data.Position = 0;
            _data.Offset = _dataOffset;

            int fieldIndex = 0;

            T obj = new();

            foreach (var f in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Type type = f.FieldType;

                int? Id_or_Ref = default;

                if (Regex.IsMatch(f.Name, "^_?id$", RegexOptions.IgnoreCase))
                {
                    if (!_dataHasId)
                        Id_or_Ref = Id;
                }
                else if (fieldIndex >= _fieldMeta.Length)
                {
                    if (_refId == -1)
                        continue;

                    Id_or_Ref = _refId;
                }

                if (Id_or_Ref.HasValue)
                {
                    if (type.IsEnum)
                        type = type.GetEnumUnderlyingType();

                    switch (Type.GetTypeCode(type))
                    {
                        case TypeCode.Single:
                            Cypher.Assert(Id_or_Ref >= float.MinValue && Id_or_Ref <= float.MaxValue, $"Record's Id value will lost data!");
                            f.SetValue(obj, (float)Id_or_Ref);
                            break;
                        case TypeCode.Int32:
                            Cypher.Assert(Id_or_Ref >= int.MinValue && Id_or_Ref <= int.MaxValue, $"Record's Id value will lost data!");
                            f.SetValue(obj, (int)Id_or_Ref);
                            break;
                        case TypeCode.UInt32:
                            Cypher.Assert(Id_or_Ref >= uint.MinValue && Id_or_Ref <= uint.MaxValue, $"Record's Id value will lost data!");
                            f.SetValue(obj, (uint)Id_or_Ref);
                            break;
                        case TypeCode.Int16:
                            Cypher.Assert(Id_or_Ref >= short.MinValue && Id_or_Ref <= short.MaxValue, $"Record's Id value will lost data!");
                            f.SetValue(obj, (short)Id_or_Ref);
                            break;
                        case TypeCode.UInt16:
                            Cypher.Assert(Id_or_Ref >= ushort.MinValue && Id_or_Ref <= ushort.MaxValue, $"Record's Id value will lost data!");
                            f.SetValue(obj, (ushort)Id_or_Ref);
                            break;
                        case TypeCode.Byte:
                            Cypher.Assert(Id_or_Ref >= byte.MinValue && Id_or_Ref <= byte.MaxValue, $"Record's Id value will lost data!");
                            f.SetValue(obj, (byte)Id_or_Ref);
                            break;
                        case TypeCode.SByte:
                            Cypher.Assert(Id_or_Ref >= sbyte.MinValue && Id_or_Ref <= sbyte.MaxValue, $"Record's Id value will lost data!");
                            f.SetValue(obj, (sbyte)Id_or_Ref);
                            break;
                        default:
                            throw new Exception($"Unhandled Type: {type}");
                    }
                    continue;
                }

                if (type.IsArray)
                {
                    Type arrayElementType = type.GetElementType();
                    if (arrayElementType.IsEnum)
                        arrayElementType = arrayElementType.GetEnumUnderlyingType();

                    Array atr = (Array)f.GetValue(obj);
                    switch (Type.GetTypeCode(arrayElementType))
                    {
                        case TypeCode.SByte:
                            f.SetValue(obj, GetFieldValueArray<sbyte>(fieldIndex, atr.Length));
                            break;
                        case TypeCode.Byte:
                            f.SetValue(obj, GetFieldValueArray<byte>(fieldIndex, atr.Length));
                            break;
                        case TypeCode.Int16:
                            f.SetValue(obj, GetFieldValueArray<short>(fieldIndex, atr.Length));
                            break;
                        case TypeCode.UInt16:
                            f.SetValue(obj, GetFieldValueArray<ushort>(fieldIndex, atr.Length));
                            break;
                        case TypeCode.Int32:
                            f.SetValue(obj, GetFieldValueArray<int>(fieldIndex, atr.Length));
                            break;
                        case TypeCode.UInt32:
                            f.SetValue(obj, GetFieldValueArray<uint>(fieldIndex, atr.Length));
                            break;
                        case TypeCode.Int64:
                            f.SetValue(obj, GetFieldValueArray<long>(fieldIndex, atr.Length));
                            break;
                        case TypeCode.UInt64:
                            f.SetValue(obj, GetFieldValueArray<ulong>(fieldIndex, atr.Length));
                            break;
                        case TypeCode.Single:
                            f.SetValue(obj, GetFieldValueArray<float>(fieldIndex, atr.Length));
                            break;
                        case TypeCode.String:
                            string[] array = new string[atr.Length];

                            if (_stringsTable == null)
                            {
                                for (int i = 0; i < array.Length; i++)
                                    array[i] = _data.ReadCString();
                            }
                            else
                            {
                                var pos = _recordsOffset + (_data.Position >> 3);

                                int[] strIdx = GetFieldValueArray<int>(fieldIndex, atr.Length);

                                for (int i = 0; i < array.Length; i++)
                                    array[i] = _stringsTable.LookupByKey(pos + i * 4 + strIdx[i]);
                            }

                            f.SetValue(obj, array);
                            break;
                        case TypeCode.Object:
                            if (arrayElementType == typeof(Vector3))
                            {
                                float[] pos = GetFieldValueArray<float>(fieldIndex, atr.Length * 3);

                                Vector3[] vectors = new Vector3[atr.Length];
                                for (var i = 0; i < atr.Length; ++i)
                                    vectors[i] = new Vector3(pos[i * 3], pos[(i * 3) + 1], pos[(i * 3) + 2]);

                                f.SetValue(obj, vectors);
                            }
                            break;
                        default:
                            throw new Exception("Unhandled array Type: " + arrayElementType.Name);
                    }
                }
                else
                {
                    if (type.IsEnum)
                        type = type.GetEnumUnderlyingType();

                    switch (Type.GetTypeCode(type))
                    {
                        case TypeCode.Single:
                            f.SetValue(obj, GetFieldValue<float>(fieldIndex));
                            break;
                        case TypeCode.Int64:
                            f.SetValue(obj, GetFieldValue<long>(fieldIndex));
                            break;
                        case TypeCode.UInt64:
                            f.SetValue(obj, GetFieldValue<ulong>(fieldIndex));
                            break;
                        case TypeCode.Int32:
                            f.SetValue(obj, GetFieldValue<int>(fieldIndex));
                            break;
                        case TypeCode.UInt32:
                            f.SetValue(obj, GetFieldValue<uint>(fieldIndex));
                            break;
                        case TypeCode.Int16:
                            f.SetValue(obj, GetFieldValue<short>(fieldIndex));
                            break;
                        case TypeCode.UInt16:
                            f.SetValue(obj, GetFieldValue<ushort>(fieldIndex));
                            break;
                        case TypeCode.Byte:
                            f.SetValue(obj, GetFieldValue<byte>(fieldIndex));
                            break;
                        case TypeCode.SByte:
                            f.SetValue(obj, GetFieldValue<sbyte>(fieldIndex));
                            break;
                        case TypeCode.String:
                            if (_stringsTable == null)
                            {
                                f.SetValue(obj, _data.ReadCString());
                            }
                            else
                            {
                                var pos = _recordsOffset + (_data.Position >> 3);
                                int ofs = GetFieldValue<int>(fieldIndex);
                                f.SetValue(obj, _stringsTable.LookupByKey(pos + ofs));
                            }
                            break;
                        case TypeCode.Object:
                            if (type == typeof(LocalizedString))
                            {
                                LocalizedString localized = new();
                                if (_stringsTable == null)
                                {
                                    localized[Locale.enUS] = _data.ReadCString();
                                }
                                else
                                {
                                    var pos = _recordsOffset + (_data.Position >> 3);
                                    int ofs = GetFieldValue<int>(fieldIndex);
                                    localized[Locale.enUS] = _stringsTable.LookupByKey(pos + ofs);
                                }

                                f.SetValue(obj, localized);
                            }
                            else if (type == typeof(Vector2))
                            {
                                float[] pos = GetFieldValueArray<float>(fieldIndex, 2);
                                f.SetValue(obj, new Vector2(pos));
                            }
                            else if (type == typeof(Vector3))
                            {
                                float[] pos = GetFieldValueArray<float>(fieldIndex, 3);
                                f.SetValue(obj, new Vector3(pos));
                            }
                            else if (type == typeof(FlagArray128))
                            {
                                uint[] flags = GetFieldValueArray<uint>(fieldIndex, 4);
                                f.SetValue(obj, new FlagArray128(flags));
                            }
                            break;
                    }
                }

                fieldIndex++;
            }

            return obj;
        }

        public WDC4Row Clone()
        {
            return (WDC4Row)MemberwiseClone();
        }
    }

    public class WDCHeader
    {
        public bool HasIndexTable()
        {
            return (Flags & HeaderFlags.Index) != 0;
        }

        public bool HasOffsetTable()
        {
            return (Flags & HeaderFlags.Sparse) != 0;
        }

        public uint Signature;
        public int RecordCount;
        public int FieldCount;
        public int RecordSize;
        public int StringTableSize;

        public uint TableHash;
        public uint LayoutHash;
        public int MinId;
        public int MaxId;
        public int Locale;
        public HeaderFlags Flags;
        public int IdIndex;
        public int TotalFieldCount;
        public int BitpackedDataOffset;
        public int LookupColumnCount;
        public int ColumnMetaSize;
        public int CommonDataSize;
        public int PalletDataSize;
        public int SectionsCount;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FieldMetaData
    {
        public short Bits;
        public ushort Offset;

        public int ByteCount
        {
            get
            {
                int value = (32 - Bits) >> 3;
                return (value < 0 ? Math.Abs(value) + 4 : value);
            }
        }

        public int BitCount
        {
            get
            {
                int bitSize = 32 - Bits;
                if (bitSize < 0)
                    bitSize = (bitSize * -1) + 32;
                return bitSize;
            }
        }

        public FieldMetaData(short bits, ushort offset)
        {
            Bits = bits;
            Offset = offset;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct ColumnMetaData
    {
        [FieldOffset(0)]
        public ushort RecordOffset;
        [FieldOffset(2)]
        public ushort Size;
        [FieldOffset(4)]
        public int AdditionalDataSize;
        [FieldOffset(8)]
        public DB2ColumnCompression CompressionType;
        [FieldOffset(12)]
        public ColumnCompressionData_Immediate Immediate;
        [FieldOffset(12)]
        public ColumnCompressionData_Pallet Pallet;
        [FieldOffset(12)]
        public ColumnCompressionData_Common Common;
    }

    public struct ColumnCompressionData_Immediate
    {
        public int BitOffset;
        public int BitWidth;
        public int Flags; // 0x1 signed
    }

    public struct ColumnCompressionData_Pallet
    {
        public int BitOffset;
        public int BitWidth;
        public int Cardinality;
    }

    public struct ColumnCompressionData_Common
    {
        public Value32 DefaultValue;
        public int B;
        public int C;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SectionHeader
    {
        public ulong TactKeyLookup;
        public int FileOffset;
        public int NumRecords;
        public int StringTableSize;
        public int OffsetRecordsEndOffset; // CatalogDataOffset, absolute value, {uint offset, ushort size}[MaxId - MinId + 1]
        public int IndexDataSize; // int indexData[IndexDataSize / 4]
        public int ParentLookupDataSize; // uint NumRecords, uint minId, uint maxId, {uint id, uint index}[NumRecords], questionable usefulness...
        public int OffsetMapIDCount;
        public int CopyTableCount;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SparseEntry
    {
        public int Offset;
        public ushort Size;
    }

    public struct ReferenceEntry
    {
        public int Id;
        public int Index;
    }

    public class ReferenceData
    {
        public int NumRecords { get; set; }
        public int MinId { get; set; }
        public int MaxId { get; set; }
        public Dictionary<int, int> Entries { get; set; }
    }

    public struct Value32
    {
        private int Value;

        public T As<T>() where T : unmanaged
        {
            return Unsafe.As<int, T>(ref Value);
        }
    }

    public class LocalizedString
    {
        public bool HasString(Locale locale = SharedConst.DefaultLocale)
        {
            return !string.IsNullOrEmpty(stringStorage[(int)locale]);
        }

        public string this[Locale locale]
        {
            get
            {
                return stringStorage[(int)locale] ?? string.Empty;
            }
            set
            {
                stringStorage[(int)locale] = value;
            }
        }

        StringArray stringStorage = new((int)Locale.Total);
    }
}
