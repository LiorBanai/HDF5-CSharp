using HDF.PInvoke;
using HDF5CSharp.DataTypes;
using PureHDF;
using PureHDF.VOL.Native;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace HDF5CSharp
{
    public partial class Hdf5
    {
        public static T ReadMandatoryObject<T>(long groupId, T targetObjectToFill, string groupName)
        {
            if (targetObjectToFill == null)
            {
                throw new ArgumentNullException(nameof(targetObjectToFill));
            }

            string normalized = Hdf5Utils.NormalizedName(groupName);
            if (!GroupExists(groupId, normalized))
            {
                string error = $"Group {groupId}. Name:{normalized} does not exist";
                Hdf5Utils.LogMessage(error, Hdf5LogLevel.Error);
                throw new Hdf5Exception(error);
            }
            Type tyObject = targetObjectToFill.GetType();
            bool isGroupName = !string.IsNullOrWhiteSpace(groupName);
            if (isGroupName)
            {
                groupId = H5G.open(groupId, normalized);
            }

            ReadFields(tyObject, targetObjectToFill, groupId);
            ReadProperties(tyObject, targetObjectToFill, groupId);

            if (isGroupName)
            {
                if (CloseGroup(groupId) != 0)
                {
                    Hdf5Utils.LogMessage($"Error closing group: {groupName}({groupId})", Hdf5LogLevel.Error);
                }
            }

            return targetObjectToFill;
        }

        public static T ReadObject<T>(long groupId, T targetObjectToFill, string groupName)
        {
            if (targetObjectToFill == null)
            {
                throw new ArgumentNullException(nameof(targetObjectToFill));
            }

            string normalized = Hdf5Utils.NormalizedName(groupName);
            if (!GroupExists(groupId, normalized))
            {
                if (Settings.ThrowOnNonExistNameWhenReading)
                {
                    string error = $"Group {groupId}. Name:{normalized} does not exist";
                    Hdf5Utils.LogMessage(error, Hdf5LogLevel.Error);
                    throw new Hdf5Exception(error);
                }
                return default(T);
            }
            Type tyObject = targetObjectToFill.GetType();
            bool isGroupName = !string.IsNullOrWhiteSpace(groupName);
            if (isGroupName)
            {
                groupId = H5G.open(groupId, normalized);
            }

            ReadFields(tyObject, targetObjectToFill, groupId);
            ReadProperties(tyObject, targetObjectToFill, groupId);

            if (isGroupName)
            {
                if (CloseGroup(groupId) != 0)
                {
                    Hdf5Utils.LogMessage($"Error closing group: {groupName}({groupId})", Hdf5LogLevel.Error);
                }
            }

            return targetObjectToFill;
        }

        public static T ReadObject<T>(long groupId, string groupName) where T : new()
        {
            T readValue = new T();
            return ReadObject(groupId, readValue, groupName);
        }
        public static T ReadObject<T>(long groupId, string groupName, bool mandatoryElement) where T : new()
        {
            T readValue = new T();
            return mandatoryElement
                ? ReadMandatoryObject(groupId, readValue, groupName)
                : ReadObject(groupId, readValue, groupName);
        }

        private static bool SkipReadProcessing(Attribute[] attributes)
        {
            bool skip;
            Hdf5ReadWriteAttribute readWriteAttribute = null;
            Hdf5SaveAttribute saveAttribute = null;
            foreach (Attribute attr in attributes)
            {
                if (attr is Hdf5ReadWriteAttribute ra)
                {
                    readWriteAttribute = ra;
                }
                else if (attr is Hdf5SaveAttribute sa)
                {
                    saveAttribute = sa;
                }
            }

            if (readWriteAttribute != null)
            {
                skip = (readWriteAttribute.ReadKind == Hdf5ReadWrite.WriteOnly ||
                        readWriteAttribute.ReadKind == Hdf5ReadWrite.DoNothing);
            }
            else if (saveAttribute != null)
            {
                Hdf5Save kind = saveAttribute.SaveKind;
                skip = (kind == Hdf5Save.DoNotSave);
            }
            else
            {
                skip = false;
            }

            return skip;
        }
        private static bool SkipSaveProcessing(Attribute[] attributes)
        {
            bool skip;
            Hdf5ReadWriteAttribute readWriteAttribute = null;
            Hdf5SaveAttribute saveAttribute = null;
            foreach (Attribute attr in attributes)
            {
                if (attr is Hdf5ReadWriteAttribute ra)
                {
                    readWriteAttribute = ra;
                }
                else if (attr is Hdf5SaveAttribute sa)
                {
                    saveAttribute = sa;
                }
            }

            if (readWriteAttribute != null)
            {
                Hdf5ReadWrite kind = readWriteAttribute.ReadKind;
                skip = (kind == Hdf5ReadWrite.ReadOnly || kind == Hdf5ReadWrite.DoNothing);
            }
            else if (saveAttribute != null)
            {
                Hdf5Save kind = saveAttribute.SaveKind;
                skip = (kind == Hdf5Save.DoNotSave);
            }
            else
            {
                skip = false;
            }

            return skip;
        }
        private static void ReadFields(Type tyObject, object targetObjectToFill, long groupId)
        {
            FieldInfo[] miMembers = tyObject.GetFields(BindingFlags.DeclaredOnly |
                                                       BindingFlags.Public |
                                                       BindingFlags.Instance);

            foreach (FieldInfo info in miMembers)
            {
                bool nextInfo = SkipReadProcessing(Attribute.GetCustomAttributes(info));
                if (nextInfo)
                {
                    continue;
                }

                (string alternativeName, bool mandatoryElement) = CheckAttribute(Attribute.GetCustomAttributes(info));

                Type ty = info.FieldType;
                TypeCode code = Type.GetTypeCode(ty);
                string name = info.Name;
                Hdf5Utils.LogMessage($"groupName: {tyObject.Name}; field name: {name}", Hdf5LogLevel.Debug);
                bool success;
                Array values;
                if (ty.IsGenericType &&
                    ty.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    ty = ty.GetGenericArguments()[0];
                    code = Type.GetTypeCode(ty);
                }
                if (ty.IsArray)
                {
                    var elType = ty.GetElementType();
                    TypeCode elCode = Type.GetTypeCode(elType);

                    if (elCode != TypeCode.Object)
                    {
                        (success, values) = dsetRW.ReadArray(elType, groupId, name, alternativeName, mandatoryElement);
                    }
                    else
                    {
                        values = CallByReflection<Array>(nameof(ReadCompounds), elType,
                            new object[] { groupId, name, alternativeName, mandatoryElement });
                        success = true;
                    }

                    if (success)
                    {
                        info.SetValue(targetObjectToFill, values);
                    }
                }

                else if (ty.IsGenericType && ty.GetGenericTypeDefinition() == typeof(List<>))
                {
                    var elType = Hdf5Utils.GetEnumerableType(ty);
                    TypeCode elCode = Type.GetTypeCode(elType);
                    if (elCode != TypeCode.Object)
                    {
                        (success, values) = dsetRW.ReadArray(elType, groupId, name, alternativeName, mandatoryElement);
                        if (success)
                        {
                            Type genericClass = typeof(List<>);

                            // MakeGenericType is badly named
                            Type constructedClass = genericClass.MakeGenericType(elType);

                            IList created = (IList)Activator.CreateInstance(constructedClass);
                            foreach (var o in values)
                            {
                                created.Add(o);
                            }

                            info.SetValue(targetObjectToFill, created);
                        }
                    }
                    else
                    {
                        var result = CallByReflection<object>(nameof(ReadCompounds), elType,
                            new object[] { groupId, name, alternativeName, mandatoryElement });
                        info.SetValue(targetObjectToFill, result);
                    }
                }
#if NET
                else if (ty == typeof(Half) || ty == typeof(TimeOnly) || ty == typeof(DateOnly))
                {
                    (success, values) = dsetRW.ReadArray(ty, groupId, name, alternativeName, mandatoryElement);

                    // get first value depending on rank of the matrix
                    int[] first = new int[values.Rank].Select(f => 0).ToArray();
                    if (success)
                    {
                        info.SetValue(targetObjectToFill, values.GetValue(first));
                    }
                }
#endif
                else if (primitiveTypes.Contains(code) || ty == typeof(TimeSpan))
                {
                    (success, values) = dsetRW.ReadArray(ty, groupId, name, alternativeName, mandatoryElement);

                    // get first value depending on rank of the matrix
                    int[] first = new int[values.Rank].Select(f => 0).ToArray();
                    if (success)
                    {
                        info.SetValue(targetObjectToFill, values.GetValue(first));
                    }
                }
                else
                {
                    object value = info.GetValue(targetObjectToFill);
                    if (value != null)
                    {
                        object result = mandatoryElement
                            ? ReadMandatoryObject(groupId, value, name)
                            : ReadObject(groupId, value, name);
                        info.SetValue(targetObjectToFill, result);
                    }
                    else
                    {
                        var nonNull = Activator.CreateInstance(ty);
                        var result = mandatoryElement
                            ? ReadMandatoryObject(groupId, nonNull, name)
                            : ReadObject(groupId, nonNull, name);
                        if (result != default)
                        {
                            info.SetValue(targetObjectToFill, nonNull);
                        }
                    }
                }
            }
        }

        private static void ReadProperties(Type tyObject, object targetObjectToFill, long groupId)
        {
            PropertyInfo[] miMembers = tyObject.GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

            foreach (PropertyInfo info in miMembers)
            {
                bool nextInfo = SkipReadProcessing(Attribute.GetCustomAttributes(info));
                if (nextInfo)
                {
                    continue;
                }

                (string alternativeName, bool mandatoryElement) = CheckAttribute(Attribute.GetCustomAttributes(info));

                Type ty = info.PropertyType;
                TypeCode code = Type.GetTypeCode(ty);
                string name = info.Name;
                bool success;
                Array values;
                if (ty.IsGenericType &&
                    ty.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    ty = ty.GetGenericArguments()[0];
                    code = Type.GetTypeCode(ty);
                }
                if (ty.IsArray)
                {
                    var elType = ty.GetElementType();
                    TypeCode elCode = Type.GetTypeCode(elType);

                    if (elCode != TypeCode.Object || ty == typeof(TimeSpan[]))
                    {
                        (success, values) = dsetRW.ReadArray(elType, groupId, name, alternativeName, mandatoryElement);
                        if (success)
                        {
                            info.SetValue(targetObjectToFill, values);
                        }
                    }
                    else
                    {
                        var obj = CallByReflection<IEnumerable>(nameof(ReadCompounds), elType,
                            new object[] { groupId, name, alternativeName, mandatoryElement });
                        var objArr = (obj).Cast<object>().ToArray();
                        values = Array.CreateInstance(elType, objArr.Length);
                        Array.Copy(objArr, values, objArr.Length);
                        info.SetValue(targetObjectToFill, values);
                    }
                }
                else if (ty.IsGenericType && ty.GetGenericTypeDefinition() == typeof(List<>))
                {
                    var elType = Hdf5Utils.GetEnumerableType(ty);
                    TypeCode elCode = Type.GetTypeCode(elType);
                    if (elCode != TypeCode.Object)
                    {
                        (success, values) = dsetRW.ReadArray(elType, groupId, name, alternativeName, mandatoryElement);
                        if (success)
                        {
                            Type genericClass = typeof(List<>);

                            // MakeGenericType is badly named
                            Type constructedClass = genericClass.MakeGenericType(elType);

                            IList created = (IList)Activator.CreateInstance(constructedClass);
                            foreach (var o in values)
                            {
                                created.Add(o);
                            }

                            info.SetValue(targetObjectToFill, created);
                        }
                    }
                    else
                    {
                        var result = CallByReflection<object>(nameof(ReadCompounds), elType,
                            new object[] { groupId, name, alternativeName, mandatoryElement });
                        info.SetValue(targetObjectToFill, result);
                    }
                }
#if NET
                else if (ty == typeof(Half) || ty == typeof(TimeOnly) || ty == typeof(DateOnly))
                {
                    (success, values) = dsetRW.ReadArray(ty, groupId, name, alternativeName, mandatoryElement);
                    if (success && values.Length > 0)
                    {
                        int[] first = new int[values.Rank].Select(f => 0).ToArray();
                        if (info.CanWrite)
                        {
                            info.SetValue(targetObjectToFill, values.GetValue(first));
                        }
                        else
                        {
                            Hdf5Utils.LogMessage($"property {info.Name} is read only. cannot set value",
                                Hdf5LogLevel.Warning);
                        }
                    }
                }
#endif
                else if (primitiveTypes.Contains(code) || ty == typeof(TimeSpan))
                {
                    (success, values) = dsetRW.ReadArray(ty, groupId, name, alternativeName, mandatoryElement);
                    if (success && values.Length > 0)
                    {
                        int[] first = new int[values.Rank].Select(f => 0).ToArray();
                        if (info.CanWrite)
                        {
                            info.SetValue(targetObjectToFill, values.GetValue(first));
                        }
                        else
                        {
                            Hdf5Utils.LogMessage($"property {info.Name} is read only. cannot set value",
                                Hdf5LogLevel.Warning);
                        }
                    }
                }
                else
                {
                    object value = info.GetValue(targetObjectToFill, null);
                    if (value != null)
                    {
                        object result = mandatoryElement
                            ? ReadMandatoryObject(groupId, value, name)
                            : ReadObject(groupId, value, name);
                        info.SetValue(targetObjectToFill, result);
                    }
                    else
                    {
                        var nonNull = Activator.CreateInstance(ty);
                        object result = mandatoryElement
                            ? ReadMandatoryObject(groupId, nonNull, name)
                            : ReadObject(groupId, nonNull, name);
                        if (result != default)
                        {
                            info.SetValue(targetObjectToFill, nonNull);
                        }
                    }
                }
            }
        }

        public static Hdf5Element ReadTreeFileStructure(string fileName)
        {
            var tree = ReadFileStructure(fileName).Tree;
            try
            {
                using (var root = H5File.OpenRead(fileName))
                {
                    AddAttributes(tree, root, true);
                }
            }
            catch (Exception e)
            {
                Hdf5Utils.LogMessage($"Error Reading file attributes: {e.Message}", Hdf5LogLevel.Error);
            }

            return tree;
        }
        public static List<Hdf5Element> ReadFlatFileStructure(string fileName)
        {
            var result = ReadFileStructure(fileName);
            try
            {
                using (var root = H5File.OpenRead(fileName))
                {
                    AddAttributes(result.Tree, root, true);
                }
            }
            catch (Exception e)
            {
                Hdf5Utils.LogMessage($"Error Reading file attributes: {e.Message}", Hdf5LogLevel.Error);
            }

            return result.Flat;
        }
        public static List<Hdf5Element> ReadFlatFileStructureWithoutAttributes(string fileName)
        {
            var flat = ReadFileStructure(fileName).Flat;
            return flat;
        }
        private static void AddAttributes(Hdf5Element element, NativeFile file, bool recursive)
        {
            try
            {
                IEnumerable<IH5Attribute> attributes = Enumerable.Empty<IH5Attribute>();
                switch (element.Type)
                {
                    case Hdf5ElementType.Unknown:
                        break;
                    case Hdf5ElementType.Group:
                        attributes = file.Group(element.GetPath()).Attributes();
                        break;
                    case Hdf5ElementType.CommitedDatatype:
                        attributes = file.CommitedDatatype(element.GetPath()).Attributes();
                        break;
                    case Hdf5ElementType.Dataset:
                        attributes = file.Dataset(element.Name).Attributes();
                        break;
                    case Hdf5ElementType.Attribute:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                foreach (IH5Attribute attr in attributes)
                {
                    var val = ReadAttributeData(attr);
                    element.AddAttribute(attr.Name, val, attr.Type.Class.ToString());
                }
            }
            catch (Exception e)
            {
                element.AddAttribute("ERROR READING ATTRIBUTES", e.Message, "Unknown");
            }
            if (recursive)
            {
                foreach (Hdf5Element child in element.GetChildren())
                {
                    AddAttributes(child, file, true);
                }
            }
        }

        private static object ReadAttributeData(IH5Attribute attribute)
        {
            return (attribute.Space.Type, attribute.Type.Class, attribute.Type.Size) switch
            {
                (H5DataspaceType.Scalar, H5DataTypeClass.FloatingPoint, 4) => attribute.Read<float>(),
                (H5DataspaceType.Scalar, H5DataTypeClass.FloatingPoint, 8) => attribute.Read<double>(),
                (H5DataspaceType.Scalar, H5DataTypeClass.FixedPoint, 1) when !attribute.Type.FixedPoint.IsSigned => attribute.Read<byte>(),
                (H5DataspaceType.Scalar, H5DataTypeClass.FixedPoint, 1) when attribute.Type.FixedPoint.IsSigned => attribute.Read<sbyte>(),
                (H5DataspaceType.Scalar, H5DataTypeClass.FixedPoint, 2) when !attribute.Type.FixedPoint.IsSigned => attribute.Read<ushort>(),
                (H5DataspaceType.Scalar, H5DataTypeClass.FixedPoint, 2) when attribute.Type.FixedPoint.IsSigned => attribute.Read<short>(),
                (H5DataspaceType.Scalar, H5DataTypeClass.FixedPoint, 4) when !attribute.Type.FixedPoint.IsSigned => attribute.Read<uint>(),
                (H5DataspaceType.Scalar, H5DataTypeClass.FixedPoint, 4) when attribute.Type.FixedPoint.IsSigned => attribute.Read<int>(),
                (H5DataspaceType.Scalar, H5DataTypeClass.FixedPoint, 8) when !attribute.Type.FixedPoint.IsSigned => attribute.Read<ulong>(),
                (H5DataspaceType.Scalar, H5DataTypeClass.FixedPoint, 8) when attribute.Type.FixedPoint.IsSigned => attribute.Read<long>(),
                (H5DataspaceType.Scalar, H5DataTypeClass.VariableLength, _) => attribute.Read<string>(),
                (H5DataspaceType.Scalar, H5DataTypeClass.String, _) => attribute.Read<string>(),
                (H5DataspaceType.Scalar,  H5DataTypeClass.Compound, _) => attribute.Read<Dictionary<string, object>>(),
                (H5DataspaceType.Simple, H5DataTypeClass.FloatingPoint, 4) => attribute.Read<float[]>(),
                (H5DataspaceType.Simple, H5DataTypeClass.FloatingPoint, 8) => attribute.Read<double[]>(),
                (H5DataspaceType.Simple, H5DataTypeClass.FixedPoint, 1) when !attribute.Type.FixedPoint.IsSigned => attribute.Read<byte[]>(),
                (H5DataspaceType.Simple, H5DataTypeClass.FixedPoint, 1) when attribute.Type.FixedPoint.IsSigned => attribute.Read<sbyte[]>(),
                (H5DataspaceType.Simple, H5DataTypeClass.FixedPoint, 2) when !attribute.Type.FixedPoint.IsSigned => attribute.Read<ushort[]>(),
                (H5DataspaceType.Simple, H5DataTypeClass.FixedPoint, 2) when attribute.Type.FixedPoint.IsSigned => attribute.Read<short[]>(),
                (H5DataspaceType.Simple, H5DataTypeClass.FixedPoint, 4) when !attribute.Type.FixedPoint.IsSigned => attribute.Read<uint[]>(),
                (H5DataspaceType.Simple, H5DataTypeClass.FixedPoint, 4) when attribute.Type.FixedPoint.IsSigned => attribute.Read<int[]>(),
                (H5DataspaceType.Simple, H5DataTypeClass.FixedPoint, 8) when !attribute.Type.FixedPoint.IsSigned => attribute.Read<ulong[]>(),
                (H5DataspaceType.Simple, H5DataTypeClass.FixedPoint, 8) when attribute.Type.FixedPoint.IsSigned => attribute.Read<long[]>(),
                (H5DataspaceType.Simple, H5DataTypeClass.VariableLength, _) => attribute.Read<string[]>(),
                (H5DataspaceType.Simple, H5DataTypeClass.String, _) => attribute.Read<string[]>(),
                (H5DataspaceType.Simple, H5DataTypeClass.Compound, _) => attribute.Read<Dictionary<string, object>>(),

                // Other types might currently be a bit difficult to read automatically.
                // However, in future it will be possible to also read unknown data by simply
                // calling attribute.Read(). This method will be part of the final release.
                //
                // If you need to support more exotic HDF types, you could use reflection
                // to get the full data type information and not just what is currently
                // being exposed in the public API. E.g. some types like "Array" or "Enum"
                // have base type information (e.g. an Enum value could be based on a uint16
                // value) which would allow you to read these kind of attributes, too.
                _ => $"The type {attribute.Space.Type}, class {attribute.Type.Class}, size {attribute.Type.Size} is currently not supported.",
            };
        }

        internal static (Hdf5Element Tree, List<Hdf5Element> Flat) ReadFileStructure(string fileName)
        {
            var elements = new List<Hdf5Element>();
            if (!File.Exists(fileName))
            {
                Hdf5Utils.LogMessage($"File {fileName} does not exist", Hdf5LogLevel.Error);
                return (new Hdf5Element("/", Hdf5ElementType.Unknown, null, -1), elements);
            }

            long fileId = H5F.open(fileName, H5F.ACC_RDONLY);
            var root = H5G.open(fileId, "/");
            var rootGroup = new Hdf5Element("/", Hdf5ElementType.Group, null, root);
            elements.Add(rootGroup);
            H5G.close(root);
            if (fileId < 0)
            {
                Hdf5Utils.LogMessage($"Could not open file {fileName}", Hdf5LogLevel.Error);
                return (rootGroup, elements);
            }

            try
            {
                StringBuilder filePath = new StringBuilder(260);
                H5F.get_name(fileId, filePath, new IntPtr(260));
                ulong idx = 0;
                bool reEnableErrors = Settings.H5InternalErrorLoggingEnabled;

                Settings.EnableH5InternalErrorReporting(false);
                H5L.iterate(fileId, H5.index_t.NAME, H5.iter_order_t.INC, ref idx, Callback,
                    Marshal.StringToHGlobalAnsi("/"));
                Settings.EnableH5InternalErrorReporting(reEnableErrors);
            }
            catch (Exception e)
            {
                Hdf5Utils.LogMessage($"Error during reading file structure of {fileName}. Error:{e}", Hdf5LogLevel.Error);
            }
            finally
            {
                if (fileId > 0)
                {
                    H5F.close(fileId);
                }
            }

            int Callback(long elementId, IntPtr intPtrName, ref H5L.info_t info, IntPtr intPtrUserData)
            {
                ulong idx2 = 0;
                long groupId = -1;
                long datasetId = -1;
                H5O.type_t objectType;
                var name = Marshal.PtrToStringAnsi(intPtrName);
                var userData = Marshal.PtrToStringAnsi(intPtrUserData);
                var fullName = CombinePath(userData, name);
                Hdf5ElementType elementType = Hdf5ElementType.Unknown;

                // this is necessary, since H5Oget_info_by_name is slow because it wants verbose object header data 
                // and H5G_loc_info is not directly accessible
                // only chance is to modify source code (H5Oget_info_by_name)
                groupId = (H5L.exists(elementId, name) >= 0) ? H5G.open(elementId, name) : -1L;
                if (H5I.is_valid(groupId) > 0)
                {
                    objectType = H5O.type_t.GROUP;
                    elementType = Hdf5ElementType.Group;
                }
                else
                {
                    datasetId = H5D.open(elementId, name);
                    if ((H5I.is_valid(datasetId) > 0))
                    {
                        objectType = H5O.type_t.DATASET;
                        elementType = Hdf5ElementType.Dataset;
                    }
                    else
                    {
                        //objectType = H5O.type_t.UNKNOWN;
                        //elementType = Hdf5ElementType.Unknown;
                        objectType = H5O.type_t.NAMED_DATATYPE;
                        elementType = Hdf5ElementType.CommitedDatatype;
                    }
                }

                var parent = elements.FirstOrDefault(e =>
                {
                    var index = fullName.LastIndexOf("/", StringComparison.Ordinal);
                    var partial = fullName.Substring(0, index);
                    return partial.Equals(e.Name);
                });

                if (parent == null)
                {
                    parent = elements.FirstOrDefault(e => e.Name == "/");
                }
                var element = new Hdf5Element(fullName, elementType, parent, elementId);
                parent.AddChild(element);
                elements.Add(element);

                if (objectType == H5O.type_t.GROUP)
                {
                    H5L.iterate(groupId, H5.index_t.NAME, H5.iter_order_t.INC, ref idx2, Callback,
                        Marshal.StringToHGlobalAnsi(fullName));
                }

                // clean up
                if (H5I.is_valid(groupId) > 0)
                {
                    H5G.close(groupId);
                }

                if (H5I.is_valid(datasetId) > 0)
                {
                    H5D.close(datasetId);
                }

                return 0;
            }
            return (rootGroup, elements);
        }

        private static string CombinePath(string path1, string path2)
        {
            return $"{path1}/{path2}".Replace("///", "/").Replace("//", "/");
        }

        public static TabularData<T> Read2DTable<T>(long groupId, string datasetName) where T : new()
        {
            TabularData<T> table = new TabularData<T>();

            Type ty = typeof(T[,]);
            bool success;
            Array values;
            if (ty.IsGenericType && ty.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                ty = ty.GetGenericArguments()[0];
            }
            if (ty.IsArray)
            {
                var elType = ty.GetElementType();
                TypeCode elCode = Type.GetTypeCode(elType);
                if (elCode != TypeCode.Object || ty == typeof(TimeSpan[]))
                {
                    (success, values) = dsetRW.ReadArray(elType, groupId, datasetName, "", false);
                    table.ReadSuccessful = success;
                    if (success)
                    {
                        table.Data = (T[,])values;
                        table.HDF5Name = datasetName;
                    }
                }
                else
                {
                    var obj = CallByReflection<IEnumerable>(nameof(ReadCompounds), elType,
                        new object[] { groupId, datasetName, "", false });
                    var objArr = (obj).Cast<object>().ToArray();
                    values = Array.CreateInstance(elType, objArr.Length);
                    Array.Copy(objArr, values, objArr.Length);
                    table.Data = (T[,])values;
                }
            }

            return table;
        }
    }
}