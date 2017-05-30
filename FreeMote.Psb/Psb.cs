﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;

//PSB format is based on psbfile by number201724.

namespace FreeMote.Psb
{
    /// <summary>
    /// P S Binary
    /// </summary>
    public class Psb
    {
        private List<string> NamesCheck;

        /// <summary>
        /// Header
        /// </summary>
        internal PsbHeader Header;
        internal PsbArray Charset;
        internal PsbArray NamesData;
        internal PsbArray NameIndexes;
        /// <summary>
        /// Names
        /// </summary>
        public List<string> Names;
        internal PsbArray StringOffsets;
        /// <summary>
        /// Strings
        /// </summary>
        public SortedDictionary<uint, PsbString> Strings;
        internal PsbArray ChunkOffsets;
        internal PsbArray ChunkLengths;
        /// <summary>
        /// Resource Chunk
        /// </summary>
        public List<PsbResource> Resources;

        /// <summary>
        /// Objects (Entries)
        /// </summary>
        public PsbDictionary Objects;

        internal PsbCollection ExpireSuffixList;
        public string Extension { get; internal set; }

        public Psb(string path)
        {

        }

        public Psb(Stream stream)
        {
            BinaryReader br = new BinaryReader(stream, Encoding.UTF8);

            //Load Header
            Header = PsbHeader.Load(br);

            //Pre Load Strings
            br.BaseStream.Seek(Header.OffsetStrings, SeekOrigin.Begin);
            StringOffsets = new PsbArray(br.ReadByte() - (byte)PsbType.ArrayN1 + 1, br);
            Strings = new SortedDictionary<uint, PsbString>();

            //Load Names
            br.BaseStream.Seek(Header.OffsetNames, SeekOrigin.Begin);
            Charset = new PsbArray(br.ReadByte() - (byte)PsbType.ArrayN1 + 1, br);
            NamesData = new PsbArray(br.ReadByte() - (byte)PsbType.ArrayN1 + 1, br);
            NameIndexes = new PsbArray(br.ReadByte() - (byte)PsbType.ArrayN1 + 1, br);
            LoadNames();
            
            //Pre Load Resources (Chunks)
            br.BaseStream.Seek(Header.OffsetChunkOffsets, SeekOrigin.Begin);
            ChunkOffsets = new PsbArray(br.ReadByte() - (byte)PsbType.ArrayN1 + 1, br);
            br.BaseStream.Seek(Header.OffsetChunkLengths, SeekOrigin.Begin);
            ChunkLengths = new PsbArray(br.ReadByte() - (byte)PsbType.ArrayN1 + 1, br);
            Resources = new List<PsbResource>(ChunkLengths.Value.Count);

            //Load Entries
            br.BaseStream.Seek(Header.OffsetEntries, SeekOrigin.Begin);
            IPsbValue obj;

            try
            {
                obj = Unpack(br);
                if (obj == null)
                {
                    throw new Exception("Can not parse objects");
                }
                if (!(obj is PsbDictionary))
                {
                    throw new Exception("Wrong offset when parsing objects");
                }
                if (Objects == null)
                {
                    Objects = obj as PsbDictionary;
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }

            if (ExpireSuffixList != null && ExpireSuffixList.Value.Count > 0)
            {
                var extStr = ExpireSuffixList.Value[0] as PsbString;
                if (extStr != null)
                {
                    Extension = extStr.Value;
                }
            }

            Resources.Sort((s1, s2) => (int)s1.Index - (int)s2.Index);
        }


        private void LoadNames()
        {
            Names = new List<string>(NameIndexes.Value.Count);
            for (int i = 0; i < NameIndexes.Value.Count; i++)
            {
                var list = new List<byte>();
                var index = NameIndexes[i];
                var chr = NamesData[(int)index];
                char temp = (char)0;
                while (chr != 0)
                {
                    var code = NamesData[(int)chr];
                    var d = Charset[(int)code];
                    var realChr = chr - d;
                    //Debug.Write(realChr.ToString("X2") + " ");
                    chr = code;

                    list.Insert(0, (byte)realChr);
                }
                //Debug.WriteLine("");
                var str = Encoding.UTF8.GetString(list.ToArray());
                Names.Add(str);

                //Seems conflict
                //if (!Strings.ContainsKey(index))
                //{
                //    Strings.Add(index, new PsbString(str, index));
                //}
            }
            NamesCheck = new List<string>(Names);
        }

        private IPsbValue Unpack(BinaryReader br)
        {
            var typeByte = br.ReadByte();
            if (!Enum.IsDefined(typeof(PsbType), typeByte))
            {
                return null;
                //throw new ArgumentOutOfRangeException($"0x{type:X2} is not a known type.");
            }
            var type = (PsbType)typeByte;
            switch (type)
            {
                case PsbType.None:
                    return null;
                case PsbType.Null:
                    return new PsbNull();
                case PsbType.False:
                case PsbType.True:
                    return new PsbBool(type == PsbType.True);
                case PsbType.NumberN0:
                case PsbType.NumberN1:
                case PsbType.NumberN2:
                case PsbType.NumberN3:
                case PsbType.NumberN4:
                case PsbType.NumberN5:
                case PsbType.NumberN6:
                case PsbType.NumberN7:
                case PsbType.NumberN8:
                case PsbType.Float0:
                case PsbType.Float:
                case PsbType.Double:
                    return new PsbNumber(type, br);
                case PsbType.ArrayN1:
                case PsbType.ArrayN2:
                case PsbType.ArrayN3:
                case PsbType.ArrayN4:
                case PsbType.ArrayN5:
                case PsbType.ArrayN6:
                case PsbType.ArrayN7:
                case PsbType.ArrayN8:
                    return new PsbArray(typeByte - (byte)PsbType.ArrayN1 + 1, br);
                case PsbType.StringN1:
                case PsbType.StringN2:
                case PsbType.StringN3:
                case PsbType.StringN4:
                    var str = new PsbString(typeByte - (byte)PsbType.StringN1 + 1, br);
                    LoadString(str, br);
                    return str;
                case PsbType.ResourceN1:
                case PsbType.ResourceN2:
                case PsbType.ResourceN3:
                case PsbType.ResourceN4:
                    var res = new PsbResource(typeByte - (byte)PsbType.ResourceN1 + 1, br);
                    LoadResource(res, br);
                    return res;
                case PsbType.Collection:
                    return LoadCollection(br);
                case PsbType.Objects:
                    return LoadObjects(br);
                //Compiler used
                case PsbType.Integer:
                case PsbType.String:
                case PsbType.Resource:
                case PsbType.Decimal:
                case PsbType.Array:
                case PsbType.Boolean:
                case PsbType.BTree:
                    break;
                default:
                    return null;
            }
            return null;
        }

        private PsbDictionary LoadObjects(BinaryReader br)
        {
            var names = new PsbArray(br.ReadByte() - (byte)PsbType.ArrayN1 + 1, br);
            var offsets = new PsbArray(br.ReadByte() - (byte)PsbType.ArrayN1 + 1, br);
            var pos = br.BaseStream.Position;
            PsbDictionary dictionary = new PsbDictionary(names.Value.Count);
            for (int i = 0; i < names.Value.Count; i++)
            {
                br.BaseStream.Seek(pos, SeekOrigin.Begin);
                var name = Names[(int)names[i]];
                var offset = offsets[i];
                br.BaseStream.Seek(offset, SeekOrigin.Current);
                var obj = Unpack(br);
                dictionary.Value.Add(name, obj);
                //Check
                NamesCheck.Remove(name);
            }
            if (dictionary.Value.ContainsKey("expire_suffix_list"))
            {
                ExpireSuffixList = dictionary["expire_suffix_list"] as PsbCollection;
            }
            return dictionary;
        }

        private PsbCollection LoadCollection(BinaryReader br)
        {
            var offsets = new PsbArray(br.ReadByte() - (byte)PsbType.ArrayN1 + 1, br);
            var pos = br.BaseStream.Position;
            PsbCollection collection = new PsbCollection(offsets.Value.Count);
            for (int i = 0; i < offsets.Value.Count; i++)
            {
                var offset = offsets[i];
                br.BaseStream.Seek(offset, SeekOrigin.Current);
                var obj = Unpack(br);
                if (obj != null)
                {
                    collection.Value.Add(obj);
                }
                br.BaseStream.Seek(pos, SeekOrigin.Begin);
            }
            return collection;
        }

        private void LoadResource(PsbResource res, BinaryReader br)
        {
            var pos = br.BaseStream.Position;
            var offset = ChunkOffsets[(int)res.Index];
            var length = ChunkLengths[(int)res.Index];
            br.BaseStream.Seek(Header.OffsetChunkData + offset, SeekOrigin.Begin);
            res.Data = br.ReadBytes((int)length);
            br.BaseStream.Seek(pos, SeekOrigin.Begin);
            Resources.Add(res);
        }

        private void LoadString(PsbString str, BinaryReader br)
        {
            if (StringOffsets == null)
            {
                return;
            }
            var pos = br.BaseStream.Position;
            br.BaseStream.Seek(Header.OffsetStringsData + StringOffsets[(int)str.Index], SeekOrigin.Begin);
            str.Value = br.ReadStringZeroTrim();
            br.BaseStream.Seek(pos, SeekOrigin.Begin);
            if (!Strings.ContainsKey(str.Index))
            {
                Strings.Add(str.Index, str);
            }
            else if(Strings[str.Index].Value == str.Value)
            {
                //Good
            }
            else
            {
                Debug.WriteLine($"[Conflict] Index:{str.Index} Exists:{Strings[str.Index]} New:{str}");
            }
        }
    }
}






