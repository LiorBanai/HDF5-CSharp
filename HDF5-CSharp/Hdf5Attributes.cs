using HDF.PInvoke;
using HDF5CSharp.DataTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HDF5CSharp
{
    public static partial class Hdf5
    {
        private static Hdf5ReaderWriter attrRW = new Hdf5ReaderWriter(new Hdf5AttributeRW());
        public static Dictionary<string, List<string>> Attributes(Type type)
        {
            Dictionary<string, List<string>> attributes = new Dictionary<string, List<string>>();

            int attributeNum = 0;
            foreach (Attribute attr in Attribute.GetCustomAttributes(type))
            {
                switch (attr)
                {
                    case Hdf5EntryNameAttribute hdf5EntryNameAttribute:
                        attributes.Add($"attribute {attributeNum++}: {hdf5EntryNameAttribute.Name}", new List<string>() { hdf5EntryNameAttribute.Name });
                        break;
                    case Hdf5Attributes hdf5Attributes:
                        attributes.Add($"attribute {attributeNum++}", hdf5Attributes.Names.ToList());
                        break;
                    case Hdf5Attribute hdf5Attribute:
                        attributes.Add($"attribute {attributeNum++}", new List<string>() { hdf5Attribute.Name });
                        break;
                    case Hdf5KeyValuesAttributes hdf5KeyValuesAttribute:
                        attributes.Add(hdf5KeyValuesAttribute.Key, hdf5KeyValuesAttribute.Values.ToList());
                        break;
                }
            }
            return attributes;
        }
        public static Dictionary<string, List<string>> Attributes(PropertyInfo propertyInfo)
        {
            Dictionary<string, List<string>> attributes = new Dictionary<string, List<string>>();
            int attributeNum = 0;
            foreach (Attribute attr in Attribute.GetCustomAttributes(propertyInfo))
            {
                switch (attr)
                {
                    case Hdf5EntryNameAttribute hdf5EntryNameAttribute:
                        attributes.Add($"attribute {attributeNum++}: {hdf5EntryNameAttribute.Name}", new List<string>() { hdf5EntryNameAttribute.Name });
                        break;
                    case Hdf5Attributes hdf5Attributes:
                        attributes.Add($"attribute {attributeNum++}", hdf5Attributes.Names.ToList());
                        break;
                    case Hdf5Attribute hdf5Attribute:
                        attributes.Add($"attribute {attributeNum++}", new List<string>() { hdf5Attribute.Name });
                        break;
                    case Hdf5KeyValuesAttributes hdf5KeyValuesAttribute:
                        attributes.Add(hdf5KeyValuesAttribute.Key, hdf5KeyValuesAttribute.Values.ToList());
                        break;
                }
            }
            return attributes;
        }

        public static Dictionary<string, List<string>> Attributes(FieldInfo fieldInfo)
        {
            Dictionary<string, List<string>> attributes = new Dictionary<string, List<string>>();
            int attributeNum = 0;
            foreach (Attribute attr in Attribute.GetCustomAttributes(fieldInfo))
            {
                switch (attr)
                {
                    case Hdf5EntryNameAttribute hdf5EntryNameAttribute:
                        attributes.Add($"attribute {attributeNum++}: {hdf5EntryNameAttribute.Name}", new List<string>() { hdf5EntryNameAttribute.Name });
                        break;
                    case Hdf5Attributes hdf5Attributes:
                        attributes.Add($"attribute {attributeNum++}", hdf5Attributes.Names.ToList());
                        break;
                    case Hdf5Attribute hdf5Attribute:
                        attributes.Add($"attribute {attributeNum++}", new List<string>() { hdf5Attribute.Name });
                        break;
                    case Hdf5KeyValuesAttributes hdf5KeyValuesAttribute:
                        attributes.Add(hdf5KeyValuesAttribute.Key, hdf5KeyValuesAttribute.Values.ToList());
                        break;
                }
            }
            return attributes;
        }

        public static (bool Success, Array Result) ReadAttributes<T>(long groupId, string name, bool mandatory = true)
        {
            return attrRW.ReadArray<T>(groupId, name, string.Empty, mandatory);
        }

        public static T ReadAttribute<T>(long groupId, string name)
            where T : struct
        {
            Type type = typeof(T);
            var datatype = GetDatatype(type);
            var attributeId = OpenAttributeIfExists(groupId, Hdf5Utils.NormalizedName(name), string.Empty);

            if (attributeId <= 0)
            {
                string error = $"Error reading {groupId}. Name:{name}";
                Hdf5Utils.LogMessage(error, Hdf5LogLevel.Warning);
                if (Settings.ThrowOnNonExistNameWhenReading)
                {
                    Hdf5Utils.LogMessage(error, Hdf5LogLevel.Error);
                    throw new Hdf5Exception(error);
                }
                return default;
            }
            var spaceId = H5A.get_space(attributeId);
            T attribute = default;

            var typeId = H5A.get_type(attributeId);

            if (datatype == H5T.C_S1)
            {
                H5T.set_size(datatype, new IntPtr(2));
            }

            int typeSize = Marshal.SizeOf(typeof(T));
            IntPtr buffer = Marshal.AllocHGlobal(typeSize);
            H5A.read(attributeId, datatype, buffer);

            var byteArray = new byte[typeSize];
            Marshal.Copy(buffer, byteArray, 0, typeSize);
            attribute = HomeMadeConverter(byteArray, attribute);

            H5T.close(typeId);
            H5A.close(attributeId);
            H5S.close(spaceId);

            return attribute;
        }

        private static T HomeMadeConverter<T>(byte[] byteArray, T obj)
        {
            return obj switch
            {
                int => (T)(object)BitConverter.ToInt32(byteArray, 0),
                uint => (T)(object)BitConverter.ToUInt32(byteArray, 0),
                long => (T)(object)BitConverter.ToInt64(byteArray, 0),
                ulong => (T)(object)BitConverter.ToUInt64(byteArray, 0),
                double => (T)(object)BitConverter.ToDouble(byteArray, 0),
                float => (T)(object)BitConverter.ToSingle(byteArray, 0),
                short => (T)(object)BitConverter.ToInt16(byteArray, 0),
                ushort => (T)(object)BitConverter.ToUInt16(byteArray, 0),
                byte => (T)(object)byteArray[0],
                sbyte => (T)(object)(sbyte)byteArray[0],
                string => (T)(object)System.Text.Encoding.Default.GetString(byteArray),
                _ => throw new NotSupportedException($"The type is {typeof(T)} is not currently supported."),
            };
        }

        public static string ReadAttribute(long groupId, string name)
        {
            var attributeId = OpenAttributeIfExists(groupId, Hdf5Utils.NormalizedName(name), string.Empty);

            if (attributeId <= 0)
            {
                string error = $"Error reading {groupId}. Name:{name}";
                Hdf5Utils.LogMessage(error, Hdf5LogLevel.Warning);
                if (Settings.ThrowOnNonExistNameWhenReading)
                {
                    Hdf5Utils.LogMessage(error, Hdf5LogLevel.Error);
                    throw new Hdf5Exception(error);
                }
                return string.Empty;
            }
            var typeId = H5A.get_type(attributeId);

            try
            {
                var sizeData = H5T.get_size(typeId);
                var size = sizeData.ToInt32();
                byte[] strBuffer = new byte[size];

                var aTypeMem = H5T.get_native_type(typeId, H5T.direction_t.ASCEND);
                GCHandle pinnedArray = GCHandle.Alloc(strBuffer, GCHandleType.Pinned);
                H5A.read(attributeId, aTypeMem, pinnedArray.AddrOfPinnedObject());
                pinnedArray.Free();
                H5T.close(aTypeMem);

                var value = System.Text.Encoding.UTF8.GetString(strBuffer);

                return value;
            }
            catch (Exception ex)
            {
                return string.Empty;
            }
            finally
            {
                H5A.close(attributeId);

                if (typeId > 0)
                {
                    H5T.close(typeId);
                }
            }
        }

        public static (bool Success, IEnumerable<string> Items) ReadStringAttributes(long groupId, string name, string alternativeName, bool mandatory)
        {
            var nameToUse = Hdf5Utils.GetRealAttributeName(groupId, name, alternativeName);
            if (!nameToUse.Valid)
            {
                Hdf5Utils.LogMessage($"Error reading {groupId}. Name:{name}. AlternativeName:{alternativeName}", Hdf5LogLevel.Warning);
                if (mandatory || Settings.ThrowOnNonExistNameWhenReading)
                {
                    Hdf5Utils.LogMessage($"Error reading {groupId}. Name:{name}. AlternativeName:{alternativeName}", Hdf5LogLevel.Error);
                    throw new Hdf5Exception($@"unable to read {name} or {alternativeName}");
                }
                return (false, Array.Empty<string>());
            }

            var datasetId = H5A.open(groupId, nameToUse.Name);
            long typeId = H5A.get_type(datasetId);
            long spaceId = H5A.get_space(datasetId);
            long count = H5S.get_simple_extent_npoints(spaceId);
            H5S.close(spaceId);

            IntPtr[] rdata = new IntPtr[count]; // create a pointer list
            GCHandle hnd = GCHandle.Alloc(rdata, GCHandleType.Pinned); // pin the array of in memory in order to be sure that the GC does not move it since unmanaged code might access it
            H5A.read(datasetId, typeId, hnd.AddrOfPinnedObject());

            var strs = new List<string>();
            for (int i = 0; i < rdata.Length; ++i)
            {
                if (rdata[i] == IntPtr.Zero)
                {
                    continue;
                }
                int len = 0;
                while (Marshal.ReadByte(rdata[i], len) != 0) { ++len; }
                byte[] buffer = new byte[len];
                Marshal.Copy(rdata[i], buffer, 0, buffer.Length);
                string s = Hdf5Utils.ReadStringBuffer(buffer);
                strs.Add(s);

                H5.free_memory(rdata[i]);
            }

            hnd.Free();
            H5T.close(typeId);
            H5A.close(datasetId);
            return (true, strs);
        }

        public static (bool Success, Array Result) ReadPrimitiveAttributes<T>(long groupId, string name, string alternativeName, bool mandatory)
        {
            Type type = typeof(T);
            var datatype = GetDatatype(type);
            var attributeId = OpenAttributeIfExists(groupId, Hdf5Utils.NormalizedName(name),
                Hdf5Utils.NormalizedName(alternativeName));

            if (attributeId <= 0)
            {
                string error = $"Error reading {groupId}. Name:{name}. AlternativeName:{alternativeName}";
                Hdf5Utils.LogMessage(error, Hdf5LogLevel.Warning);
                if (mandatory || Settings.ThrowOnNonExistNameWhenReading)
                {
                    Hdf5Utils.LogMessage(error, Hdf5LogLevel.Error);
                    throw new Hdf5Exception(error);
                }
                return (false, Array.Empty<T>());
            }
            var spaceId = H5A.get_space(attributeId);
            int rank = H5S.get_simple_extent_ndims(spaceId);
            ulong[] maxDims = new ulong[rank];
            ulong[] dims = new ulong[rank];
            long memId = H5S.get_simple_extent_dims(spaceId, dims, maxDims);
            long[] lengths = dims.Select(d => Convert.ToInt64(d)).ToArray();
            Array attributes = Array.CreateInstance(type, lengths);

            var typeId = H5A.get_type(attributeId);

            //var mem_type = H5T.copy(datatype);
            if (datatype == H5T.C_S1)
            {
                H5T.set_size(datatype, new IntPtr(2));
            }

            //var propId = H5A.get_create_plist(attributeId);
            //memId = H5S.create_simple(rank, dims, maxDims);
            GCHandle hnd = GCHandle.Alloc(attributes, GCHandleType.Pinned);
            H5A.read(attributeId, datatype, hnd.AddrOfPinnedObject());
            hnd.Free();
            H5T.close(typeId);
            H5A.close(attributeId);
            H5S.close(spaceId);

            //H5S.close(memId);
            //H5P.close(propId);
            return (true, attributes);
        }

        public static (int Success, long AttributeId) WriteStringAttribute(long groupId, string name, string val, string groupOrDatasetName)
        {
            return WriteStringAttributes(groupId, name, new[] { val }, groupOrDatasetName);
        }

        public static (int Success, long CreatedId) WriteIntegerAttributes<T>(long groupId, string name, IEnumerable<T> values, string groupOrDatasetName = null) where T : struct
        {
            long tmpId = groupId;
            if (!string.IsNullOrWhiteSpace(groupOrDatasetName))
            {
                long datasetId = H5D.open(groupId, Hdf5Utils.NormalizedName(groupOrDatasetName));
                if (datasetId > 0)
                {
                    groupId = datasetId;
                }
            }

            // create UTF-8 encoded attributes
            long datatype = GetDatatype(typeof(T));

            int strSz = values.Count();
            long spaceId = H5S.create_simple(1, new[] { (ulong)strSz }, null);
            string normalizedName = Hdf5Utils.NormalizedName(name);

            var attributeId = Hdf5Utils.GetAttributeId(groupId, normalizedName, datatype, spaceId);
            GCHandle[] hnds = new GCHandle[strSz];
            IntPtr[] wdata = new IntPtr[strSz];

            int cntr = 0;

            foreach (T value in values)
            {
                hnds[cntr] = GCHandle.Alloc(Marshal.SizeOf(value.GetType()), GCHandleType.Pinned);
                wdata[cntr] = hnds[cntr].AddrOfPinnedObject();
                cntr++;
            }

            var hnd = GCHandle.Alloc(values.ToArray(), GCHandleType.Pinned);

            var result = H5A.write(attributeId, datatype, hnd.AddrOfPinnedObject());
            hnd.Free();

            for (int i = 0; i < strSz; ++i)
            {
                hnds[i].Free();
            }

            H5A.close(attributeId);
            H5S.close(spaceId);
            if (tmpId != groupId)
            {
                H5D.close(groupId);
            }
            return (result, attributeId);
        }

        public static (int Success, long CreatedId) WriteAsciiStringAttributes(long groupId, string name,
            IEnumerable<string> values, string groupOrDatasetName = null)
        {
            var str = values.ToArray();
            long tmpId = groupId;
            if (!string.IsNullOrWhiteSpace(groupOrDatasetName))
            {
                long datasetId = H5D.open(groupId, Hdf5Utils.NormalizedName(groupOrDatasetName));
                if (datasetId > 0)
                {
                    groupId = datasetId;
                }
            }
            int strSz = str.Count();
            long spaceId = H5S.create_simple(1, new[] { (ulong)strSz }, null);
            string normalizedName = Hdf5Utils.NormalizedName(name);
            long datatype = H5T.create(H5T.class_t.STRING, H5T.VARIABLE);

            var attributeId = Hdf5Utils.GetAttributeId(groupId, normalizedName, datatype, spaceId);

            var spaceNullId = H5S.create(H5S.class_t.NULL);
            var spaceScalarId = H5S.create(H5S.class_t.SCALAR);

            // create two datasets of the extended ASCII character set
            // store as H5T.FORTRAN_S1 -> space padding
            int strLength = str.Length;
            ulong[] dims = { (ulong)strLength, 1 };

            //byte[] wdata = new byte[strLength * 2];

            //for (int i = 0; i < strLength; ++i)
            //{
            //    wdata[2 * i] = Convert.ToByte(str[i]);
            //}
            GCHandle[] hnds = new GCHandle[strSz];
            IntPtr[] wdata = new IntPtr[strSz];

            int cntr = 0;
            foreach (string s in str)
            {
                hnds[cntr] = GCHandle.Alloc(
                    Hdf5Utils.StringToByte(s),
                    GCHandleType.Pinned);
                wdata[cntr] = hnds[cntr].AddrOfPinnedObject();
                cntr++;
            }
            var memId = H5T.copy(H5T.C_S1);
            H5T.set_size(memId, new IntPtr(2));
            GCHandle hnd = GCHandle.Alloc(wdata, GCHandleType.Pinned);
            var result = H5A.write(attributeId, datatype, hnd.AddrOfPinnedObject());

            hnd.Free();
            H5T.close(memId);
            H5A.close(attributeId);
            H5S.close(spaceId);
            H5T.close(datatype);
            if (tmpId != groupId)
            {
                H5D.close(groupId);
            }
            return (result, attributeId);
        }

        public static (int Success, long CreatedId) WriteStringAttributes(long groupId, string name, IEnumerable<string> values, string groupOrDatasetName = null)
        {
            long tmpId = groupId;
            if (!string.IsNullOrWhiteSpace(groupOrDatasetName))
            {
                long datasetId = H5D.open(groupId, Hdf5Utils.NormalizedName(groupOrDatasetName));
                if (datasetId > 0)
                {
                    groupId = datasetId;
                }
            }

            // create UTF-8 encoded attributes
            long datatype = H5T.create(H5T.class_t.STRING, H5T.VARIABLE);
            H5T.set_cset(datatype, Hdf5Utils.GetCharacterSet(Settings.CharacterSetType));
            H5T.set_strpad(datatype, Hdf5Utils.GetCharacterPadding(Settings.CharacterPaddingType));

            int strSz = values.Count();
            long spaceId = H5S.create_simple(1, new[] { (ulong)strSz }, null);
            string normalizedName = Hdf5Utils.NormalizedName(name);

            var attributeId = Hdf5Utils.GetAttributeId(groupId, normalizedName, datatype, spaceId);
            GCHandle[] hnds = new GCHandle[strSz];
            IntPtr[] wdata = new IntPtr[strSz];

            int cntr = 0;
            foreach (string str in values)
            {
                hnds[cntr] = GCHandle.Alloc(
                    Hdf5Utils.StringToByte(str),
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

        public static (int Success, long CreatedId) WriteAttributes<T>(long groupId, string name, T attribute) //where T : struct
        {
            return WriteAttributes<T>(groupId, name, new T[1] { attribute });
        }

        public static (int Success, long CreatedId) WriteAttributes<T>(long groupId, string name, Array attributes)
        {
            return attrRW.WriteArray(groupId, name, attributes, new Dictionary<string, List<string>>());

            //if (attributes.GetType().GetElementType() == typeof(string))
            //     WriteStringAttributes(groupId, name, attributes.Cast<string>(), attributeName);
            //else
            //    WritePrimitiveAttribute<T>(groupId, name, attributes, attributeName);
        }

        public static (int Success, long CreatedgroupId) WritePrimitiveAttribute<T>(long groupId, string name, Array attributes) //where T : struct
        {
            var tmpId = groupId;
            int rank = attributes.Rank;
            ulong[] dims = Enumerable.Range(0, rank).Select(i => (ulong)attributes.GetLength(i)).ToArray();
            ulong[] maxDims = null;
            var spaceId = H5S.create_simple(rank, dims, maxDims);
            var datatype = GetDatatype(typeof(T));
            var typeId = H5T.copy(datatype);
            string nameToUse = Hdf5Utils.NormalizedName(name);

            //var attributeId = H5A.create(groupId, nameToUse, datatype, spaceId);
            var attributeId = Hdf5Utils.GetAttributeId(groupId, nameToUse, datatype, spaceId);
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

        public static (int Success, long CreatedId) WriteAttribute(long groupId, string name, string value)
        {
            long attributeSpace = 0;
            long stringId = 0;
            long attributeId = 0;

            try
            {
                attributeSpace = H5S.create(H5S.class_t.SCALAR);
                stringId = H5T.copy(H5T.C_S1);
                H5T.set_size(stringId, new IntPtr(value.Length));
                attributeId = H5A.create(groupId, name, stringId, attributeSpace);

                //H5P.set_char_encoding(attributeId, H5T.cset_t.UTF8);

                IntPtr descriptionArray = Marshal.StringToHGlobalAnsi(value);

                //IntPtr descriptionArray2 = Marshal.StringToHGlobalUni(value);
                var result = H5A.write(attributeId, stringId, descriptionArray);

                Marshal.FreeHGlobal(descriptionArray);

                return (result, attributeId);
            }
            catch (Exception ex)
            {
                return (-1, attributeId);
            }
            finally
            {
                if (attributeId != 0)
                {
                    H5A.close(attributeId);
                }

                if (stringId != 0)
                {
                    H5T.close(stringId);
                }

                if (attributeSpace != 0)
                {
                    H5S.close(attributeSpace);
                }
            }
        }

        public static (int Success, long CreatedId) WriteAttribute<T>(long groupId, string name, T value)
            where T : struct
        {
            long attributeSpace = 0;
            long typeId = 0;
            long attributeId = 0;

            try
            {
                attributeSpace = H5S.create(H5S.class_t.SCALAR);
                typeId = H5T.copy(GetHdfType(value));
                H5T.set_size(typeId, new IntPtr(Marshal.SizeOf<T>()));
                attributeId = H5A.create(groupId, name, typeId, attributeSpace);

                GCHandle handle = GCHandle.Alloc(value, GCHandleType.Pinned);
                IntPtr ptr = handle.AddrOfPinnedObject();
                var result = H5A.write(attributeId, typeId, ptr);

                handle.Free();

                return (result, attributeId);
            }
            catch (Exception ex)
            {
                return (-1, attributeId);
            }
            finally
            {
                if (attributeId != 0)
                {
                    H5A.close(attributeId);
                }

                if (typeId != 0)
                {
                    H5T.close(typeId);
                }

                if (attributeSpace != 0)
                {
                    H5S.close(attributeSpace);
                }
            }
        }

        private static long GetHdfType<T>(T value)
        {
            return value switch
            {
                // bool, decimal and char does not work for writing
                //bool => H5T.NATIVE_HBOOL,
                //decimal => H5T.NATIVE_LDOUBLE,
                //char => H5T.NATIVE_CHAR,
                byte => H5T.NATIVE_UINT8,
                sbyte => H5T.NATIVE_INT8,
                double => H5T.NATIVE_DOUBLE,
                float => H5T.NATIVE_FLOAT,
                int => H5T.NATIVE_INT,
                uint => H5T.NATIVE_UINT,
                long => H5T.NATIVE_LLONG,
                ulong => H5T.NATIVE_ULLONG,
                short => H5T.NATIVE_SHORT,
                ushort => H5T.NATIVE_USHORT,
                _ => throw new NotSupportedException(),
            };
        }
    }
}