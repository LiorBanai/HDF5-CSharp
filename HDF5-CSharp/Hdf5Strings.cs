using HDF.PInvoke;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using HDF5CSharp.DataTypes;

namespace HDF5CSharp
{
    public static partial class Hdf5
    {
        public static (bool success, IEnumerable<string> result) ReadStrings(long groupId, string name, string alternativeName, bool mandatory)
        {
            long datasetId = OpenDatasetIfExists(groupId, Hdf5Utils.NormalizedName(name),
                                                Hdf5Utils.NormalizedName(alternativeName));
            if (datasetId < 0) //does not exist?
            {
                Hdf5Utils.LogMessage($"Warning reading {groupId}. Name:{name}. AlternativeName:{alternativeName}", Hdf5LogLevel.Warning);
                if (mandatory || Settings.ThrowOnNonExistNameWhenReading)
                {
                    string error = $"Error reading {groupId}. Name:{name}. AlternativeName:{alternativeName}";
                    Hdf5Utils.LogMessage(error, Hdf5LogLevel.Error);
                    throw new Hdf5Exception(error);
                }
                return (false, Array.Empty<string>());
            }
            long typeId = H5D.get_type(datasetId);
            long spaceId = H5D.get_space(datasetId);
            long count = H5S.get_simple_extent_npoints(spaceId);
            H5S.close(spaceId);

            var strs = new List<string>();
            if (count >= 0)
            {
                IntPtr[] rdata = new IntPtr[count];
                GCHandle hnd = GCHandle.Alloc(rdata, GCHandleType.Pinned);
                H5D.read(datasetId, typeId, H5S.ALL, H5S.ALL,
                    H5P.DEFAULT, hnd.AddrOfPinnedObject());

                for (int i = 0; i < rdata.Length; ++i)
                {
                    int len = 0;
                    while (Marshal.ReadByte(rdata[i], len) != 0) { ++len; }
                    byte[] buffer = new byte[len];
                    Marshal.Copy(rdata[i], buffer, 0, buffer.Length);
                    string s = Hdf5Utils.ReadStringBuffer(buffer);

                    strs.Add(s);

                    // H5.free_memory(rdata[i]);
                }
                hnd.Free();
            }
            H5T.close(typeId);
            H5D.close(datasetId);
            return (true, strs);
        }


        public static (int success, long CreatedgroupId) WriteStrings(long groupId, string name, IEnumerable<string> strs)
        {

            // create UTF-8 encoded test datasets

            long datatype = H5T.create(H5T.class_t.STRING, H5T.VARIABLE);
            H5T.set_cset(datatype, Hdf5Utils.GetCharacterSet(Settings.CharacterSetType));
            H5T.set_strpad(datatype, Hdf5Utils.GetCharacterPadding(Settings.CharacterPaddingType));

            int strSz = strs.Count();
            long spaceId = H5S.create_simple(1, new[] { (ulong)strSz }, null);

            string normalizedName = Hdf5Utils.NormalizedName(name);
            var datasetId = Hdf5Utils.GetDatasetId(groupId, normalizedName, datatype, spaceId, H5P.DEFAULT);
            if (datasetId == -1L)
            {
                return (-1, -1L);
            }

            GCHandle[] hnds = new GCHandle[strSz];
            IntPtr[] wdata = new IntPtr[strSz];

            int cntr = 0;
            foreach (string str in strs)
            {
                hnds[cntr] = GCHandle.Alloc(
                    Hdf5Utils.StringToByte(str),
                    GCHandleType.Pinned);
                wdata[cntr] = hnds[cntr].AddrOfPinnedObject();
                cntr++;
            }

            var hnd = GCHandle.Alloc(wdata, GCHandleType.Pinned);

            var result = H5D.write(datasetId, datatype, H5S.ALL, H5S.ALL,
                H5P.DEFAULT, hnd.AddrOfPinnedObject());
            hnd.Free();

            for (int i = 0; i < strSz; ++i)
            {
                hnds[i].Free();
            }

            H5D.close(datasetId);
            H5S.close(spaceId);
            H5T.close(datatype);
            return (result, datasetId);
        }

        public static int WriteAsciiString(long groupId, string name, string str)
        {
			var spaceScalarId = H5S.create(H5S.class_t.SCALAR);

			int strLength = str.Length;

			var memId = H5T.copy(H5T.C_S1);
			// Set the size needed for the string. Leave one extra space for a null-terminated string
			H5T.set_size(memId, new IntPtr(strLength + 1));

			var datasetId = H5D.create(groupId, Hdf5Utils.NormalizedName(name), memId, spaceScalarId);

			byte[] wdata = new byte[strLength];
			// Write the string to the buffer, with the last element being 0 as the string terminator
			for (int i = 0; i < strLength; ++i)
			{
				wdata[i] = Convert.ToByte(str[i]);
			}

			GCHandle hnd = GCHandle.Alloc(wdata, GCHandleType.Pinned);

			int result = H5D.write(datasetId, memId, H5S.ALL, H5S.ALL, H5P.DEFAULT, hnd.AddrOfPinnedObject());
			hnd.Free();
			H5S.close(spaceScalarId);
			H5T.close(memId);
			H5D.close(datasetId);
			return result;
		}

        public static string ReadAsciiString(long groupId, string name)
        {
            var datasetId = H5D.open(groupId, Hdf5Utils.NormalizedName(name));
            var spaceId = H5D.get_space(datasetId);
            
            ulong spaceNeeded = H5D.get_storage_size(datasetId);
			byte[] wdata = new byte[spaceNeeded];

            GCHandle hnd = GCHandle.Alloc(wdata, GCHandleType.Pinned);
			var memId = H5T.copy(H5T.C_S1);
			H5T.set_size(memId, new IntPtr((int)spaceNeeded));

			int resultId = H5D.read(datasetId, memId, H5S.ALL,
                        H5S.ALL, H5P.DEFAULT, hnd.AddrOfPinnedObject());
            hnd.Free();

            // Remove the null termination of the string
            wdata = wdata.Where(b => b != 0).ToArray();
            string result = Encoding.ASCII.GetString(wdata);

            H5T.close(memId);
            H5D.close(datasetId);
            return result;
        }

        public static int WriteUnicodeString(long groupId, string name, string str, H5T.str_t strPad = H5T.str_t.SPACEPAD)
        {
            byte[] wdata = Hdf5Utils.StringToByte(str);

            long spaceId = H5S.create(H5S.class_t.SCALAR);

            long dtype = H5T.create(H5T.class_t.STRING, new IntPtr(wdata.Length));
            H5T.set_cset(dtype, Hdf5Utils.GetCharacterSet(Settings.CharacterSetType));
            H5T.set_strpad(dtype, strPad);

            long datasetId = H5D.create(groupId, Hdf5Utils.NormalizedName(name), dtype, spaceId);

            GCHandle hnd = GCHandle.Alloc(wdata, GCHandleType.Pinned);
            int result = H5D.write(datasetId, dtype, H5S.ALL,
                H5S.ALL, H5P.DEFAULT, hnd.AddrOfPinnedObject());
            hnd.Free();

            H5T.close(dtype);
            H5D.close(datasetId);
            H5S.close(spaceId);
            return result;
        }

        public static string ReadUnicodeString(long groupId, string name)
        {
            var datasetId = H5D.open(groupId, Hdf5Utils.NormalizedName(name));
            var typeId = H5D.get_type(datasetId);

            if (H5T.is_variable_str(typeId) > 0)
            {
                var spaceId = H5D.get_space(datasetId);
                long count = H5S.get_simple_extent_npoints(spaceId);

                IntPtr[] rdata = new IntPtr[count];

                GCHandle hnd = GCHandle.Alloc(rdata, GCHandleType.Pinned);
                H5D.read(datasetId, typeId, H5S.ALL, H5S.ALL,
                    H5P.DEFAULT, hnd.AddrOfPinnedObject());

                var attrStrings = new List<string>();
                for (int i = 0; i < rdata.Length; ++i)
                {
                    int attrLength = 0;
                    while (Marshal.ReadByte(rdata[i], attrLength) != 0)
                    {
                        ++attrLength;
                    }

                    byte[] buffer = new byte[attrLength];
                    Marshal.Copy(rdata[i], buffer, 0, buffer.Length);

                    string stringPart = Hdf5Utils.ReadStringBuffer(buffer);

                    attrStrings.Add(stringPart);

                    H5.free_memory(rdata[i]);
                }

                hnd.Free();
                H5S.close(spaceId);
                H5D.close(datasetId);

                return attrStrings[0];
            }

            // Must be a non-variable length string.
            int size = H5T.get_size(typeId).ToInt32();
            IntPtr iPtr = Marshal.AllocHGlobal(size);

            int result = H5D.read(datasetId, typeId, H5S.ALL, H5S.ALL,
                H5P.DEFAULT, iPtr);
            if (result < 0)
            {
                throw new IOException("Failed to read dataset");
            }

            var strDest = new byte[size];
            Marshal.Copy(iPtr, strDest, 0, size);
            Marshal.FreeHGlobal(iPtr);

            H5D.close(datasetId);

            return Hdf5Utils.ReadStringBuffer(strDest).TrimEnd((char)0);
        }
    }

}
