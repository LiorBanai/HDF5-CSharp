﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using HDF.PInvoke;

namespace Hdf5DotnetWrapper
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class Hdf5GroupName : Attribute
    {

        public Hdf5GroupName(string name)
        {
            Name = name;
        }

        public string Name { get; private set; }
    }

    public sealed class Hdf5Attributes : Attribute
    {

        public Hdf5Attributes(string[] names)
        {
            Names = names;
        }

        public string[] Names { get; private set; }
    }

    public sealed class Hdf5Attribute : Attribute
    {

        public Hdf5Attribute(string name)
        {
            Name = name;
        }

        public string Name { get; private set; }
    }

    public static partial class Hdf5
    {
        private static Hdf5ReaderWriter attrRW = new Hdf5ReaderWriter(new Hdf5AttributeRW());

        public static Array ReadAttributes<T>(Int64 groupId, string name)
        {
            return attrRW.ReadArray<T>(groupId, name, string.Empty);
        }

        public static T ReadAttribute<T>(Int64 groupId, string name)
        {
            var attrs = attrRW.ReadArray<T>(groupId, name, string.Empty);
            int[] first = new int[attrs.Rank].Select(f => 0).ToArray();
            T result = (T)attrs.GetValue(first);
            return result;
        }

        public static IEnumerable<string> ReadStringAttributes(Int64 groupId, string name)
        {

            Int64 datatype = H5T.create(H5T.class_t.STRING, H5T.VARIABLE);
            H5T.set_cset(datatype, H5T.cset_t.UTF8);
            H5T.set_strpad(datatype, H5T.str_t.NULLTERM);

            //name = ToHdf5Name(name);

            var datasetId = H5A.open(groupId, Hdf5Utils.NormalizedName(name));
            Int64 spaceId = H5A.get_space(datasetId);

            long count = H5S.get_simple_extent_npoints(spaceId);
            H5S.close(spaceId);

            IntPtr[] rdata = new IntPtr[count];
            GCHandle hnd = GCHandle.Alloc(rdata, GCHandleType.Pinned);
            H5A.read(datasetId, datatype, hnd.AddrOfPinnedObject());

            var strs = new List<string>();
            for (int i = 0; i < rdata.Length; ++i)
            {
                int len = 0;
                while (Marshal.ReadByte(rdata[i], len) != 0) { ++len; }
                byte[] buffer = new byte[len];
                Marshal.Copy(rdata[i], buffer, 0, buffer.Length);
                string s = Encoding.UTF8.GetString(buffer);

                strs.Add(s);

                H5.free_memory(rdata[i]);
            }

            hnd.Free();
            H5T.close(datatype);
            H5A.close(datasetId);
            return strs;
        }

        public static Array ReadPrimitiveAttributes<T>(Int64 groupId, string name, string alternativeName) //where T : struct
        {
            Type type = typeof(T);
            var datatype = GetDatatype(type);

            var attributeId = H5A.open(groupId, Hdf5Utils.NormalizedName(name));
            if (attributeId <= 0)
                attributeId = H5A.open(groupId, Hdf5Utils.NormalizedName(alternativeName));
            var spaceId = H5A.get_space(attributeId);
            int rank = H5S.get_simple_extent_ndims(spaceId);
            ulong[] maxDims = new ulong[rank];
            ulong[] dims = new ulong[rank];
            Int64 memId = H5S.get_simple_extent_dims(spaceId, dims, maxDims);
            long[] lengths = dims.Select(d => Convert.ToInt64(d)).ToArray();
            Array attributes = Array.CreateInstance(type, lengths);

            var typeId = H5A.get_type(attributeId);
            var mem_type = H5T.copy(datatype);
            if (datatype == H5T.C_S1)
                H5T.set_size(datatype, new IntPtr(2));

            var propId = H5A.get_create_plist(attributeId);

            memId = H5S.create_simple(rank, dims, maxDims);
            GCHandle hnd = GCHandle.Alloc(attributes, GCHandleType.Pinned);
            H5A.read(attributeId, datatype, hnd.AddrOfPinnedObject());
            hnd.Free();
            H5A.close(typeId);
            H5A.close(attributeId);
            H5S.close(spaceId);
            return attributes;
        }

        public static (int success, Int64 attributeId) WriteStringAttribute(Int64 groupId, string name, string str, string datasetName = null)
        {
            return WriteStringAttributes(groupId, name, new[] { str }, datasetName);
        }

        public static (int success, Int64 CreatedgroupId) WriteStringAttributes(Int64 groupId, string name, IEnumerable<string> strs, string datasetName = null)
        {
            Int64 tmpId = groupId;
            if (!string.IsNullOrWhiteSpace(datasetName))
            {
                Int64 datasetId = H5D.open(groupId, Hdf5Utils.NormalizedName(datasetName));
                if (datasetId > 0)
                    groupId = datasetId;
            }

            // create UTF-8 encoded attributes
            Int64 datatype = H5T.create(H5T.class_t.STRING, H5T.VARIABLE);
            H5T.set_cset(datatype, H5T.cset_t.UTF8);
            H5T.set_strpad(datatype, H5T.str_t.SPACEPAD);

            int strSz = strs.Count();
            Int64 spaceId = H5S.create_simple(1, new[] { (ulong)strSz }, null);

            var attributeId = H5A.create(groupId, Hdf5Utils.NormalizedName(name), datatype, spaceId);

            GCHandle[] hnds = new GCHandle[strSz];
            IntPtr[] wdata = new IntPtr[strSz];

            int cntr = 0;
            foreach (string str in strs)
            {
                hnds[cntr] = GCHandle.Alloc(
                    Encoding.UTF8.GetBytes(str),
                    GCHandleType.Pinned);
                wdata[cntr] = hnds[cntr].AddrOfPinnedObject();
                cntr++;
            }

            var hnd = GCHandle.Alloc(wdata, GCHandleType.Pinned);

            var result = H5A.write(attributeId, datatype, hnd.AddrOfPinnedObject());
            hnd.Free();

            for (int i = 0; i < strSz; ++i)
            {
                hnds[i].Free();
            }

            H5A.close(attributeId);
            H5S.close(spaceId);
            H5T.close(datatype);
            if (tmpId != groupId)
            {
                H5D.close(groupId);
            }
            return (result, attributeId);
        }

        public static void WriteAttribute<T>(Int64 groupId, string name, T attribute, string datasetName = null) //where T : struct
        {
            WriteAttributes<T>(groupId, name, new T[1] { attribute }, datasetName);
            /*if (typeof(T) == typeof(string))
                attrRW.WriteArray(groupId, name, new T[1] { attribute });
            else
            {
                Array oneVal = new T[1, 1] { { attribute } };
                attrRW.WriteArray(groupId, name, oneVal);
            }*/
        }

        public static void WriteAttributes<T>(Int64 groupId, string name, Array attributes, string datasetName = null) //
        {
            attrRW.WriteArray(groupId, name, attributes, datasetName);
            /* if (attributes.GetType().GetElementType() == typeof(string))
                 return WriteStringAttributes(groupId, name, attributes.Cast<string>(), datasetName);
             else
                 return WritePrimitiveAttribute<T>(groupId, name, attributes, datasetName);*/
        }

        public static (int success, Int64 CreatedgroupId) WritePrimitiveAttribute<T>(Int64 groupId, string name, Array attributes, string datasetName = null) //where T : struct
        {
            var tmpId = groupId;
            if (!string.IsNullOrWhiteSpace(datasetName))
            {
                var datasetId = H5D.open(groupId, datasetName);
                if (datasetId > 0)
                    groupId = datasetId;
            }
            int rank = attributes.Rank;
            ulong[] dims = Enumerable.Range(0, rank).Select(i =>
                { return (ulong)attributes.GetLength(i); }).ToArray();
            ulong[] maxDims = null;
            var spaceId = H5S.create_simple(rank, dims, maxDims);
            var datatype = GetDatatype(typeof(T));
            var typeId = H5T.copy(datatype);
            var attributeId = H5A.create(groupId, Hdf5Utils.NormalizedName(name), datatype, spaceId);
            GCHandle hnd = GCHandle.Alloc(attributes, GCHandleType.Pinned);
            var result = H5A.write(attributeId, datatype, hnd.AddrOfPinnedObject());
            hnd.Free();

            H5A.close(attributeId);
            H5S.close(spaceId);
            H5T.close(typeId);
            if (tmpId != groupId)
            {
                H5D.close(groupId);
            }
            return (result, attributeId);
        }
    }

    public enum Hdf5Save
    {
        Save,
        DoNotSave
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public sealed class Hdf5SaveAttribute : Attribute
    {
        private readonly Hdf5Save saveKind;

        public Hdf5Save SaveKind => saveKind;      // Topic is a named parameter


        public Hdf5SaveAttribute(Hdf5Save saveKind)  // url is a positional parameter
        {
            this.saveKind = saveKind;
        }

    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public sealed class Hdf5EntryNameAttribute : Attribute
    {
        public string Name { get; }


        public Hdf5EntryNameAttribute(string name)
        {
            Name = name;
        }

    }
}