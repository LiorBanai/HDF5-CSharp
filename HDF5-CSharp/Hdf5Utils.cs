using HDF.PInvoke;
using HDF5CSharp.DataTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace HDF5CSharp
{
    public static class Hdf5Utils
    {
        public static Action<string> LogError;
        public static Action<string> LogInfo;
        public static Action<string> LogDebug;
        public static Action<string> LogWarning;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (bool Valid, string Name) GetRealName(long id, string name, string alternativeName)
        {
            string normalized = NormalizedName(name);
            if (!string.IsNullOrEmpty(normalized) && H5L.exists(id, normalized) > 0)
            {
                return (true, normalized);
            }

            normalized = NormalizedName(alternativeName);
            if (!string.IsNullOrEmpty(normalized) && H5L.exists(id, normalized) > 0)
            {
                return (true, normalized);
            }

            return (false, "");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (bool Valid, string Name) GetRealAttributeName(long id, string name, string alternativeName)
        {
            string normalized = NormalizedName(name);
            if (!string.IsNullOrEmpty(normalized) && H5A.exists(id, normalized) > 0)
            {
                return (true, normalized);
            }

            normalized = NormalizedName(alternativeName);
            if (!string.IsNullOrEmpty(normalized) && H5A.exists(id, normalized) > 0)
            {
                return (true, normalized);
            }

            return (false, "");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string NormalizedName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }
            return Hdf5.Settings.LowerCaseNaming ? name.ToLowerInvariant() : name;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void LogMessage(string msg, Hdf5LogLevel level)
        {
            if (!Hdf5.Settings.GlobalLoggingEnabled)
            {
                return;
            }

            switch (level)
            {
                case Hdf5LogLevel.Debug:
                    LogDebug?.Invoke(msg);
                    break;
                case Hdf5LogLevel.Info:
                    LogInfo?.Invoke(msg);
                    break;
                case Hdf5LogLevel.Warning:
                    LogWarning?.Invoke(msg);
                    break;
                case Hdf5LogLevel.Error:
                    LogError?.Invoke(msg);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(level), level, null);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ItemExists(long groupId, string groupName, Hdf5ElementType type)
        {
            switch (type)
            {
                case Hdf5ElementType.Group:
                case Hdf5ElementType.Dataset:
                    return H5L.exists(groupId, NormalizedName(groupName)) > 0;
                case Hdf5ElementType.Attribute:
                    return H5A.exists(groupId, NormalizedName(groupName)) > 0;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }
        internal static long GetDatasetId(long parentId, string name, long dataType, long spaceId, long propId)
        {
            return GetId(parentId, name, dataType, spaceId, propId, Hdf5ElementType.Dataset);
        }
        internal static long GetAttributeId(long parentId, string name, long dataType, long spaceId)
        {
            return GetId(parentId, name, dataType, spaceId, H5P.DEFAULT, Hdf5ElementType.Attribute);
        }

        private static long GetId(long parentId, string name, long dataType, long spaceId, long propId, Hdf5ElementType type)
        {
            string normalizedName = NormalizedName(name);
            bool exists = ItemExists(parentId, normalizedName, type);
            if (exists)
            {
                LogMessage($"{normalizedName} already exists", Hdf5LogLevel.Debug);
                if (!Hdf5.Settings.OverrideExistingData)
                {
                    if (Hdf5.Settings.ThrowOnError)
                    {
                        throw new Hdf5Exception($"{normalizedName} already exists");
                    }

                    return -1;
                }
            }

            var datasetId = -1L;
            switch (type)
            {
                case Hdf5ElementType.Unknown:
                    break;
                case Hdf5ElementType.Group:
                case Hdf5ElementType.Dataset:
                    if (exists)
                    {
                        H5L.delete(parentId, normalizedName);
                    }
                    datasetId = H5D.create(parentId, normalizedName, dataType, spaceId, H5P.DEFAULT, propId);

                    break;
                case Hdf5ElementType.Attribute:
                    if (exists)
                    {
                        H5A.delete(parentId, normalizedName);
                    }

                    datasetId = H5A.create(parentId, normalizedName, dataType, spaceId);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }

            if (datasetId == -1L)
            {
                string error = $"Unable to create data set for {normalizedName}";
                LogMessage($"{normalizedName} already exists", Hdf5LogLevel.Error);
                if (Hdf5.Settings.ThrowOnError)
                {
                    throw new Hdf5Exception(error);
                }
            }
            return datasetId;
        }

        public static Type GetEnumerableType(Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return type.GetGenericArguments()[0];
            }

            var iface = (type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))).FirstOrDefault();

            if (iface == null)
            {
                throw new ArgumentException($"{type} Does not represent an enumerable type.", type.Name);
            }

            return GetEnumerableType(iface);
        }

        public static (string Value, bool Success) ReadAttributeByPath(string fileName, string xpath, string attributeName)
        {
            long fileId = -1;

            try
            {
                var fileStructure = Hdf5.ReadFlatFileStructureWithoutAttributes(fileName);
                fileId = Hdf5.OpenFile(fileName, true);
                if (fileId <= 0)
                {
                    LogMessage("Invalid type", Hdf5LogLevel.Error);
                    return (string.Empty, false);
                }

                var group = fileStructure.SingleOrDefault(element => element.Name == xpath);
                if (group == null)
                {
                    LogMessage($"group {xpath} was not found", Hdf5LogLevel.Error);
                    return (string.Empty, false);
                }

                var groupAccessId = H5G.open(fileId, group.Name);
                if (groupAccessId <= 0)
                {
                    LogMessage($"unable to open group: {group.Name}", Hdf5LogLevel.Error);
                    return (string.Empty, false);
                }

                var value = Hdf5.ReadAttribute(groupAccessId, attributeName);
                return (value, true);
            }
            catch (Exception e)
            {
                LogMessage($"Error reading Attribute: {e.Message}", Hdf5LogLevel.Error);
                return (string.Empty, false);
            }
            finally
            {
                if (fileId > 0)
                {
                    H5F.close(fileId);
                }
            }
        }

        public static (string[] Values, bool Success) ReadAttributesByPath(string fileName, string xpath, string attributeName)
        {
            long fileId = -1;

            try
            {
                var fileStructure = Hdf5.ReadFlatFileStructureWithoutAttributes(fileName);
                fileId = Hdf5.OpenFile(fileName, true);
                if (fileId <= 0)
                {
                    LogMessage("Invalid type", Hdf5LogLevel.Error);
                    return ([], false);
                }

                var group = fileStructure.SingleOrDefault(element => element.Name == xpath);
                if (group == null)
                {
                    LogMessage($"group {xpath} was not found", Hdf5LogLevel.Error);
                    return ([], false);
                }

                var groupAccessId = H5G.open(fileId, group.Name);
                if (groupAccessId <= 0)
                {
                    LogMessage($"unable to open group: {group.Name}", Hdf5LogLevel.Error);
                    return ([], false);
                }

                var value = Hdf5.ReadAttributes<string>(groupAccessId, attributeName, true);
                return (value.Result.Cast<string>().ToArray(), value.Success);
            }
            catch (Exception e)
            {
                LogMessage($"Error reading Attribute: {e.Message}", Hdf5LogLevel.Error);
                return ([], false);
            }
            finally
            {
                if (fileId > 0)
                {
                    H5F.close(fileId);
                }
            }
        }

        public static bool WriteAttributeByPath(string fileName, string xpath, string attributeName, string value)
        {
            var funcWriteAttribute = (long groupAccessId) => Hdf5.WriteAttribute(groupAccessId, attributeName, value);

            return WriteAttributeByPath(fileName, xpath, funcWriteAttribute);
        }

        public static bool WriteAttributesByPath(string fileName, string xpath, string attributeName, string value)
        {
            var funcWriteAttribute = (long groupAccessId) => Hdf5.WriteAttributes(groupAccessId, attributeName, value);

            return WriteAttributeByPath(fileName, xpath, funcWriteAttribute);
        }

        private static bool WriteAttributeByPath(string fileName, string xpath, Func<long, (int Success, long CreatedId)> funcWriteAttribute)
        {
            long fileId = -1;

            try
            {
                var fileStructure = Hdf5.ReadFlatFileStructureWithoutAttributes(fileName);
                fileId = Hdf5.OpenFile(fileName, true);
                if (fileId <= 0)
                {
                    LogMessage("Invalid type", Hdf5LogLevel.Error);
                    return false;
                }

                var group = fileStructure.SingleOrDefault(element => element.Name == xpath);
                if (group == null)
                {
                    LogMessage($"group {xpath} was not found", Hdf5LogLevel.Error);
                    return false;
                }

                var groupAccessId = H5G.open(fileId, group.Name);
                if (groupAccessId <= 0)
                {
                    LogMessage($"unable to open group: {group.Name}", Hdf5LogLevel.Error);
                    return false;
                }

                var result = funcWriteAttribute(groupAccessId);
                return result.Success >= 0;
            }
            catch (Exception e)
            {
                LogMessage($"Error reading Attribute: {e.Message}", Hdf5LogLevel.Error);
                return false;
            }
            finally
            {
                if (fileId > 0)
                {
                    H5F.close(fileId);
                }
            }
        }

        internal static H5T.cset_t GetCharacterSet(CharacterSetType characterSetType)
        {
            switch (characterSetType)
            {
                case CharacterSetType.ASCII:
                    return H5T.cset_t.ASCII;
                case CharacterSetType.UTF8:
                    return H5T.cset_t.UTF8;
                default:
                    throw new ArgumentOutOfRangeException(nameof(characterSetType), characterSetType, null);
            }
        }

        internal static H5T.str_t GetCharacterPadding(CharacterPaddingType characterPaddingType)
        {
            switch (characterPaddingType)
            {
                case CharacterPaddingType.NULLTERM:
                    return H5T.str_t.NULLTERM;
                case CharacterPaddingType.NULLPAD:
                    return H5T.str_t.NULLPAD;
                case CharacterPaddingType.SPACEPAD:
                    return H5T.str_t.SPACEPAD;
                default:
                    throw new ArgumentOutOfRangeException(nameof(characterPaddingType), characterPaddingType, null);
            }
        }

        public static string ReadStringBuffer(byte[] buffer)
        {
            switch (Hdf5.Settings.CharacterSetType)
            {
                case CharacterSetType.ASCII:
                    return Encoding.ASCII.GetString(buffer);
                case CharacterSetType.UTF8:
                    return Encoding.UTF8.GetString(buffer);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static byte[] StringToByte(string str)
        {
            switch (Hdf5.Settings.CharacterSetType)
            {
                case CharacterSetType.ASCII:
                    return Encoding.ASCII.GetBytes(str);
                case CharacterSetType.UTF8:
                    return Encoding.UTF8.GetBytes(str);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        public static IEnumerable<IEnumerable<T>> Chunk<T>(this IEnumerable<T> list, int chunkSize)
        {
            if (chunkSize <= 0)
            {
                throw new ArgumentException("chunkSize must be greater than 0.");
            }

            while (list.Any())
            {
                yield return list.Take(chunkSize);
                list = list.Skip(chunkSize);
            }
        }
    }
}