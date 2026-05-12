using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Il2CppDumper
{
    public sealed class Metadata : BinaryStream
    {
        public Il2CppGlobalMetadataHeader header;
        public Il2CppImageDefinition[] imageDefs;
        public Il2CppAssemblyDefinition[] assemblyDefs;
        public Il2CppTypeDefinition[] typeDefs;
        public Il2CppMethodDefinition[] methodDefs;
        public Il2CppParameterDefinition[] parameterDefs;
        public Il2CppFieldDefinition[] fieldDefs;
        private readonly Dictionary<int, Il2CppFieldDefaultValue> fieldDefaultValuesDic;
        private readonly Dictionary<int, Il2CppParameterDefaultValue> parameterDefaultValuesDic;
        public Il2CppPropertyDefinition[] propertyDefs;
        public Il2CppCustomAttributeTypeRange[] attributeTypeRanges;
        public Il2CppCustomAttributeDataRange[] attributeDataRanges;
        private readonly Dictionary<Il2CppImageDefinition, Dictionary<uint, int>> attributeTypeRangesDic;
        public Il2CppStringLiteral[] stringLiterals;
        private readonly Il2CppMetadataUsageList[] metadataUsageLists;
        private readonly Il2CppMetadataUsagePair[] metadataUsagePairs;
        public int[] attributeTypes;
        public int[] interfaceIndices;
        public Dictionary<Il2CppMetadataUsage, SortedDictionary<uint, uint>> metadataUsageDic;
        public long metadataUsagesCount;
        public int[] nestedTypeIndices;
        public Il2CppEventDefinition[] eventDefs;
        public Il2CppGenericContainer[] genericContainers;
        public Il2CppFieldRef[] fieldRefs;
        public Il2CppGenericParameter[] genericParameters;
        public int[] constraintIndices;
        public uint[] vtableMethods;
        public Il2CppRGCTXDefinition[] rgctxEntries;

        private readonly Dictionary<uint, string> stringCache = new();
        private int typeIndexSize = 4;
        private int typeDefinitionIndexSize = 4;
        private int genericContainerIndexSize = 4;
        private int parameterIndexSize = 4;
        private int eventIndexSize = 4;
        private int interfacesIndexSize = 4;
        private int nestedTypeIndexSize = 4;
        private int propertyIndexSize = 4;
        private int methodIndexSize = 4;
        private int genericParameterIndexSize = 4;
        private int fieldIndexSize = 4;
        private int defaultValueDataIndexSize = 4;

        public Metadata(Stream stream) : base(stream)
        {
            var sanity = ReadUInt32();
            if (sanity != 0xFAB11BAF)
            {
                throw new InvalidDataException("ERROR: Metadata file supplied is not valid metadata file.");
            }
            var version = ReadInt32();
            if (version < 0 || version > 1000)
            {
                throw new InvalidDataException("ERROR: Metadata file supplied is not valid metadata file.");
            }
            if (version < 16 || version > 200)
            {
                throw new NotSupportedException($"ERROR: Metadata file supplied is not a supported version[{version}].");
            }
            Version = version;
            if (Version >= 38)
            {
                header = ReadMetadataHeaderV38();
                SetupMetadataIndexSizes();
                imageDefs = ReadMetadataClassArray<Il2CppImageDefinition>(header.images);
                assemblyDefs = ReadMetadataClassArray<Il2CppAssemblyDefinition>(header.assemblies);
                typeDefs = ReadMetadataClassArray<Il2CppTypeDefinition>(header.typeDefinitions);
                methodDefs = ReadMetadataClassArray<Il2CppMethodDefinition>(header.methods);
                parameterDefs = ReadMetadataClassArray<Il2CppParameterDefinition>(header.parameters);
                fieldDefs = ReadMetadataClassArray<Il2CppFieldDefinition>(header.fields);
            }
            else
            {
                header = ReadClass<Il2CppGlobalMetadataHeader>(0);
                if (version == 24)
                {
                    if (header.stringLiteralOffset == 264)
                    {
                        Version = 24.2;
                        header = ReadClass<Il2CppGlobalMetadataHeader>(0);
                    }
                    else
                    {
                        imageDefs = ReadMetadataClassArray<Il2CppImageDefinition>(header.imagesOffset, header.imagesSize);
                        if (imageDefs.Any(x => x.token != 1))
                        {
                            Version = 24.1;
                        }
                    }
                }
                imageDefs = ReadMetadataClassArray<Il2CppImageDefinition>(header.imagesOffset, header.imagesSize);
                if (Version == 24.2 && header.assembliesSize / 68 < imageDefs.Length)
                {
                    Version = 24.4;
                }
                var v241Plus = false;
                if (Version == 24.1 && header.assembliesSize / 64 == imageDefs.Length)
                {
                    v241Plus = true;
                }
                if (v241Plus)
                {
                    Version = 24.4;
                }
                assemblyDefs = ReadMetadataClassArray<Il2CppAssemblyDefinition>(header.assembliesOffset, header.assembliesSize);
                if (v241Plus)
                {
                    Version = 24.1;
                }
                typeDefs = ReadMetadataClassArray<Il2CppTypeDefinition>(header.typeDefinitionsOffset, header.typeDefinitionsSize);
                methodDefs = ReadMetadataClassArray<Il2CppMethodDefinition>(header.methodsOffset, header.methodsSize);
                parameterDefs = ReadMetadataClassArray<Il2CppParameterDefinition>(header.parametersOffset, header.parametersSize);
                fieldDefs = ReadMetadataClassArray<Il2CppFieldDefinition>(header.fieldsOffset, header.fieldsSize);
            }
            var fieldDefaultValues = Version >= 38
                ? ReadMetadataClassArray<Il2CppFieldDefaultValue>(header.fieldDefaultValues)
                : ReadMetadataClassArray<Il2CppFieldDefaultValue>(header.fieldDefaultValuesOffset, header.fieldDefaultValuesSize);
            var parameterDefaultValues = Version >= 38
                ? ReadMetadataClassArray<Il2CppParameterDefaultValue>(header.parameterDefaultValues)
                : ReadMetadataClassArray<Il2CppParameterDefaultValue>(header.parameterDefaultValuesOffset, header.parameterDefaultValuesSize);
            // Handle possible duplicate keys in v31+
            fieldDefaultValuesDic = fieldDefaultValues.GroupBy(x => x.fieldIndex).ToDictionary(g => g.Key, g => g.First());
            parameterDefaultValuesDic = parameterDefaultValues.GroupBy(x => x.parameterIndex).ToDictionary(g => g.Key, g => g.First());
            propertyDefs = Version >= 38
                ? ReadMetadataClassArray<Il2CppPropertyDefinition>(header.properties)
                : ReadMetadataClassArray<Il2CppPropertyDefinition>(header.propertiesOffset, header.propertiesSize);
            interfaceIndices = Version >= 38
                ? ReadMetadataIndexArray(header.interfaces, typeIndexSize)
                : ReadClassArray<int>(header.interfacesOffset, header.interfacesSize / 4);
            nestedTypeIndices = Version >= 38
                ? ReadMetadataIndexArray(header.nestedTypes, typeDefinitionIndexSize)
                : ReadClassArray<int>(header.nestedTypesOffset, header.nestedTypesSize / 4);
            eventDefs = Version >= 38
                ? ReadMetadataClassArray<Il2CppEventDefinition>(header.events)
                : ReadMetadataClassArray<Il2CppEventDefinition>(header.eventsOffset, header.eventsSize);
            genericContainers = Version >= 38
                ? ReadMetadataClassArray<Il2CppGenericContainer>(header.genericContainers)
                : ReadMetadataClassArray<Il2CppGenericContainer>(header.genericContainersOffset, header.genericContainersSize);
            genericParameters = Version >= 38
                ? ReadMetadataClassArray<Il2CppGenericParameter>(header.genericParameters)
                : ReadMetadataClassArray<Il2CppGenericParameter>(header.genericParametersOffset, header.genericParametersSize);
            constraintIndices = Version >= 38
                ? ReadMetadataIndexArray(header.genericParameterConstraints, typeIndexSize)
                : ReadClassArray<int>(header.genericParameterConstraintsOffset, header.genericParameterConstraintsSize / 4);
            vtableMethods = Version >= 38
                ? ReadClassArray<uint>((uint)header.vtableMethods.offset, header.vtableMethods.count)
                : ReadClassArray<uint>(header.vtableMethodsOffset, header.vtableMethodsSize / 4);
            stringLiterals = Version >= 38
                ? ReadMetadataClassArray<Il2CppStringLiteral>(header.stringLiterals)
                : ReadMetadataClassArray<Il2CppStringLiteral>(header.stringLiteralOffset, header.stringLiteralSize);
            if (Version > 16)
            {
                fieldRefs = Version >= 38
                    ? ReadMetadataClassArray<Il2CppFieldRef>(header.fieldRefs)
                    : ReadMetadataClassArray<Il2CppFieldRef>(header.fieldRefsOffset, header.fieldRefsSize);
                if (Version < 27)
                {
                    metadataUsageLists = ReadMetadataClassArray<Il2CppMetadataUsageList>(header.metadataUsageListsOffset, header.metadataUsageListsCount);
                    metadataUsagePairs = ReadMetadataClassArray<Il2CppMetadataUsagePair>(header.metadataUsagePairsOffset, header.metadataUsagePairsCount);

                    ProcessingMetadataUsage();
                }
            }
            if (Version > 20 && Version < 29)
            {
                attributeTypeRanges = ReadMetadataClassArray<Il2CppCustomAttributeTypeRange>(header.attributesInfoOffset, header.attributesInfoCount);
                attributeTypes = ReadClassArray<int>(header.attributeTypesOffset, header.attributeTypesCount / 4);
            }
            if (Version >= 29)
            {
                attributeDataRanges = Version >= 38
                    ? ReadMetadataClassArray<Il2CppCustomAttributeDataRange>(header.attributeDataRanges)
                    : ReadMetadataClassArray<Il2CppCustomAttributeDataRange>(header.attributeDataRangeOffset, header.attributeDataRangeSize);
            }
            if (Version > 24)
            {
                attributeTypeRangesDic = new Dictionary<Il2CppImageDefinition, Dictionary<uint, int>>();
                foreach (var imageDef in imageDefs)
                {
                    var dic = new Dictionary<uint, int>();
                    attributeTypeRangesDic[imageDef] = dic;
                    var end = imageDef.customAttributeStart + imageDef.customAttributeCount;
                    for (int i = imageDef.customAttributeStart; i < end; i++)
                    {
                        if (Version >= 29)
                        {
                            // Handle possible duplicate tokens in v31+
                            var token = attributeDataRanges[i].token;
                            if (!dic.ContainsKey(token))
                                dic.Add(token, i);
                        }
                        else
                        {
                            var token = attributeTypeRanges[i].token;
                            if (!dic.ContainsKey(token))
                                dic.Add(token, i);
                        }
                    }
                }
            }
            if (Version <= 24.1)
            {
                rgctxEntries = ReadMetadataClassArray<Il2CppRGCTXDefinition>(header.rgctxEntriesOffset, header.rgctxEntriesCount);
            }
        }

        private T[] ReadMetadataClassArray<T>(uint addr, int count) where T : new()
        {
            return ReadClassArray<T>(addr, count / SizeOf(typeof(T)));
        }

        private T[] ReadMetadataClassArray<T>(Il2CppSectionMetadata section) where T : new()
        {
            Position = (ulong)section.offset;
            var result = new T[section.count];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = ReadMetadataClass<T>();
            }
            return result;
        }

        private T ReadMetadataClass<T>() where T : new()
        {
            if (typeof(T) == typeof(Il2CppImageDefinition))
                return (T)(object)ReadImageDefinition();
            if (typeof(T) == typeof(Il2CppTypeDefinition))
                return (T)(object)ReadTypeDefinition();
            if (typeof(T) == typeof(Il2CppMethodDefinition))
                return (T)(object)ReadMethodDefinition();
            if (typeof(T) == typeof(Il2CppParameterDefinition))
                return (T)(object)ReadParameterDefinition();
            if (typeof(T) == typeof(Il2CppFieldDefinition))
                return (T)(object)ReadFieldDefinition();
            if (typeof(T) == typeof(Il2CppFieldDefaultValue))
                return (T)(object)ReadFieldDefaultValue();
            if (typeof(T) == typeof(Il2CppParameterDefaultValue))
                return (T)(object)ReadParameterDefaultValue();
            if (typeof(T) == typeof(Il2CppPropertyDefinition))
                return (T)(object)ReadPropertyDefinition();
            if (typeof(T) == typeof(Il2CppEventDefinition))
                return (T)(object)ReadEventDefinition();
            if (typeof(T) == typeof(Il2CppGenericContainer))
                return (T)(object)ReadGenericContainer();
            if (typeof(T) == typeof(Il2CppGenericParameter))
                return (T)(object)ReadGenericParameter();
            if (typeof(T) == typeof(Il2CppFieldRef))
                return (T)(object)ReadFieldRef();
            if (typeof(T) == typeof(Il2CppStringLiteral))
                return (T)(object)ReadStringLiteral();
            return ReadClass<T>();
        }

        private Il2CppGlobalMetadataHeader ReadMetadataHeaderV38()
        {
            Position = 0;
            var result = new Il2CppGlobalMetadataHeader
            {
                sanity = ReadUInt32(),
                version = ReadInt32(),
                stringLiterals = ReadClass<Il2CppSectionMetadata>(),
                stringLiteralData = ReadClass<Il2CppSectionMetadata>(),
                strings = ReadClass<Il2CppSectionMetadata>(),
                events = ReadClass<Il2CppSectionMetadata>(),
                properties = ReadClass<Il2CppSectionMetadata>(),
                methods = ReadClass<Il2CppSectionMetadata>(),
                parameterDefaultValues = ReadClass<Il2CppSectionMetadata>(),
                fieldDefaultValues = ReadClass<Il2CppSectionMetadata>(),
                fieldAndParameterDefaultValueData = ReadClass<Il2CppSectionMetadata>(),
                fieldMarshaledSizes = ReadClass<Il2CppSectionMetadata>(),
                parameters = ReadClass<Il2CppSectionMetadata>(),
                fields = ReadClass<Il2CppSectionMetadata>(),
                genericParameters = ReadClass<Il2CppSectionMetadata>(),
                genericParameterConstraints = ReadClass<Il2CppSectionMetadata>(),
                genericContainers = ReadClass<Il2CppSectionMetadata>(),
                nestedTypes = ReadClass<Il2CppSectionMetadata>(),
                interfaces = ReadClass<Il2CppSectionMetadata>(),
                vtableMethods = ReadClass<Il2CppSectionMetadata>(),
                interfaceOffsets = ReadClass<Il2CppSectionMetadata>(),
                typeDefinitions = ReadClass<Il2CppSectionMetadata>()
            };
            if (Version >= 104)
                result.typeInlineArrays = ReadClass<Il2CppSectionMetadata>();
            result.images = ReadClass<Il2CppSectionMetadata>();
            result.assemblies = ReadClass<Il2CppSectionMetadata>();
            result.fieldRefs = ReadClass<Il2CppSectionMetadata>();
            result.referencedAssemblies = ReadClass<Il2CppSectionMetadata>();
            result.attributeData = ReadClass<Il2CppSectionMetadata>();
            result.attributeDataRanges = ReadClass<Il2CppSectionMetadata>();
            result.unresolvedIndirectCallParameterTypes = ReadClass<Il2CppSectionMetadata>();
            result.unresolvedIndirectCallParameterRanges = ReadClass<Il2CppSectionMetadata>();
            result.windowsRuntimeTypeNames = ReadClass<Il2CppSectionMetadata>();
            result.windowsRuntimeStrings = ReadClass<Il2CppSectionMetadata>();
            result.exportedTypeDefinitions = ReadClass<Il2CppSectionMetadata>();
            return result;
        }

        private void SetupMetadataIndexSizes()
        {
            static int GetIndexSize(int count) => count <= byte.MaxValue ? 1 : count <= ushort.MaxValue ? 2 : 4;

            typeDefinitionIndexSize = GetIndexSize(header.typeDefinitions.count);
            genericContainerIndexSize = GetIndexSize(header.genericContainers.count);
            var actualInterfaceOffsetPairSize = header.interfaceOffsets.count == 0 ? 8 : header.interfaceOffsets.sectionSize / header.interfaceOffsets.count;
            typeIndexSize = actualInterfaceOffsetPairSize switch
            {
                8 => 4,
                6 => 2,
                5 => 1,
                _ => 4
            };
            if (Version >= 39)
                parameterIndexSize = GetIndexSize(header.parameters.count);
            if (Version >= 104)
            {
                eventIndexSize = GetIndexSize(header.events.count);
                interfacesIndexSize = GetIndexSize(header.interfaceOffsets.count);
                nestedTypeIndexSize = GetIndexSize(header.nestedTypes.count);
                propertyIndexSize = GetIndexSize(header.properties.count);
            }
            if (Version >= 105)
                methodIndexSize = GetIndexSize(header.methods.count);
            if (Version >= 106)
            {
                genericParameterIndexSize = GetIndexSize(header.genericParameters.count);
                fieldIndexSize = GetIndexSize(header.fields.count);
                defaultValueDataIndexSize = GetIndexSize(header.fieldAndParameterDefaultValueData.count);
            }
        }

        private int[] ReadMetadataIndexArray(Il2CppSectionMetadata section, int indexSize)
        {
            Position = (ulong)section.offset;
            var result = new int[section.count];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = ReadMetadataIndex(indexSize);
            }
            return result;
        }

        private int ReadMetadataIndex(int size)
        {
            if (size == 4)
                return ReadInt32();
            if (size == 2)
            {
                var value = ReadUInt16();
                return value == ushort.MaxValue ? -1 : value;
            }
            var byteValue = ReadByte();
            return byteValue == byte.MaxValue ? -1 : byteValue;
        }

        private Il2CppImageDefinition ReadImageDefinition() => new()
        {
            nameIndex = ReadUInt32(),
            assemblyIndex = ReadInt32(),
            typeStart = ReadMetadataIndex(typeDefinitionIndexSize),
            typeCount = ReadUInt32(),
            exportedTypeStart = ReadMetadataIndex(typeDefinitionIndexSize),
            exportedTypeCount = ReadUInt32(),
            entryPointIndex = ReadMetadataIndex(methodIndexSize),
            token = ReadUInt32(),
            customAttributeStart = ReadInt32(),
            customAttributeCount = ReadUInt32()
        };

        private Il2CppTypeDefinition ReadTypeDefinition() => new()
        {
            nameIndex = ReadUInt32(),
            namespaceIndex = ReadUInt32(),
            byvalTypeIndex = ReadMetadataIndex(typeIndexSize),
            declaringTypeIndex = ReadMetadataIndex(typeIndexSize),
            parentIndex = ReadMetadataIndex(typeIndexSize),
            genericContainerIndex = ReadMetadataIndex(genericContainerIndexSize),
            flags = ReadUInt32(),
            fieldStart = ReadMetadataIndex(fieldIndexSize),
            methodStart = ReadMetadataIndex(methodIndexSize),
            eventStart = ReadMetadataIndex(eventIndexSize),
            propertyStart = ReadMetadataIndex(propertyIndexSize),
            nestedTypesStart = ReadMetadataIndex(nestedTypeIndexSize),
            interfacesStart = ReadMetadataIndex(interfacesIndexSize),
            vtableStart = ReadInt32(),
            interfaceOffsetsStart = ReadMetadataIndex(interfacesIndexSize),
            method_count = ReadUInt16(),
            property_count = ReadUInt16(),
            field_count = ReadUInt16(),
            event_count = ReadUInt16(),
            nested_type_count = ReadUInt16(),
            vtable_count = ReadUInt16(),
            interfaces_count = ReadUInt16(),
            interface_offsets_count = ReadUInt16(),
            bitfield = ReadUInt32(),
            token = ReadUInt32()
        };

        private Il2CppMethodDefinition ReadMethodDefinition() => new()
        {
            nameIndex = ReadUInt32(),
            declaringType = ReadMetadataIndex(typeDefinitionIndexSize),
            returnType = ReadMetadataIndex(typeIndexSize),
            returnParameterToken = ReadInt32(),
            parameterStart = ReadMetadataIndex(parameterIndexSize),
            genericContainerIndex = ReadMetadataIndex(genericContainerIndexSize),
            token = ReadUInt32(),
            flags = ReadUInt16(),
            iflags = ReadUInt16(),
            slot = ReadUInt16(),
            parameterCount = ReadUInt16()
        };

        private Il2CppParameterDefinition ReadParameterDefinition() => new()
        {
            nameIndex = ReadUInt32(),
            token = ReadUInt32(),
            typeIndex = ReadMetadataIndex(typeIndexSize)
        };

        private Il2CppFieldDefinition ReadFieldDefinition() => new()
        {
            nameIndex = ReadUInt32(),
            typeIndex = ReadMetadataIndex(typeIndexSize),
            token = ReadUInt32()
        };

        private Il2CppFieldDefaultValue ReadFieldDefaultValue() => new()
        {
            fieldIndex = ReadMetadataIndex(fieldIndexSize),
            typeIndex = ReadMetadataIndex(typeIndexSize),
            dataIndex = ReadMetadataIndex(defaultValueDataIndexSize)
        };

        private Il2CppParameterDefaultValue ReadParameterDefaultValue() => new()
        {
            parameterIndex = ReadMetadataIndex(parameterIndexSize),
            typeIndex = ReadMetadataIndex(typeIndexSize),
            dataIndex = ReadMetadataIndex(defaultValueDataIndexSize)
        };

        private Il2CppPropertyDefinition ReadPropertyDefinition() => new()
        {
            nameIndex = ReadUInt32(),
            get = ReadMetadataIndex(methodIndexSize),
            set = ReadMetadataIndex(methodIndexSize),
            attrs = ReadUInt32(),
            token = ReadUInt32()
        };

        private Il2CppEventDefinition ReadEventDefinition() => new()
        {
            nameIndex = ReadUInt32(),
            typeIndex = ReadMetadataIndex(typeIndexSize),
            add = ReadMetadataIndex(methodIndexSize),
            remove = ReadMetadataIndex(methodIndexSize),
            raise = ReadMetadataIndex(methodIndexSize),
            token = ReadUInt32()
        };

        private Il2CppGenericContainer ReadGenericContainer()
        {
            var result = new Il2CppGenericContainer
            {
                ownerIndex = ReadInt32()
            };
            if (Version >= 106)
            {
                result.type_argc = ReadUInt16();
                ReadUInt16();
                result.is_method = ReadByte();
                ReadByte();
                ReadByte();
                ReadByte();
            }
            else
            {
                result.type_argc = ReadInt32();
                result.is_method = ReadInt32();
            }
            result.genericParameterStart = ReadMetadataIndex(genericParameterIndexSize);
            return result;
        }

        private Il2CppGenericParameter ReadGenericParameter() => new()
        {
            ownerIndex = ReadMetadataIndex(genericContainerIndexSize),
            nameIndex = ReadUInt32(),
            constraintsStart = ReadInt16(),
            constraintsCount = ReadInt16(),
            num = ReadUInt16(),
            flags = ReadUInt16()
        };

        private Il2CppFieldRef ReadFieldRef() => new()
        {
            typeIndex = ReadMetadataIndex(typeIndexSize),
            fieldIndex = ReadMetadataIndex(fieldIndexSize)
        };

        private Il2CppStringLiteral ReadStringLiteral()
        {
            if (Version <= 31)
            {
                return new Il2CppStringLiteral
                {
                    length = ReadUInt32(),
                    dataIndex = ReadInt32()
                };
            }
            return new Il2CppStringLiteral
            {
                dataIndex = ReadInt32()
            };
        }

        public bool GetFieldDefaultValueFromIndex(int index, out Il2CppFieldDefaultValue value)
        {
            return fieldDefaultValuesDic.TryGetValue(index, out value);
        }

        public bool GetParameterDefaultValueFromIndex(int index, out Il2CppParameterDefaultValue value)
        {
            return parameterDefaultValuesDic.TryGetValue(index, out value);
        }

        public uint GetDefaultValueFromIndex(int index)
        {
            var offset = Version >= 38 ? (uint)header.fieldAndParameterDefaultValueData.offset : header.fieldAndParameterDefaultValueDataOffset;
            return (uint)(offset + index);
        }

        public string GetStringFromIndex(uint index)
        {
            return TryGetStringFromIndex(index, out var result) ? result : string.Empty;
        }

        public bool TryGetStringFromIndex(uint index, out string result)
        {
            if (stringCache.TryGetValue(index, out result))
            {
                return true;
            }

            var stringOffset = Version >= 38 ? (uint)header.strings.offset : header.stringOffset;
            var stringSize = Version >= 38 ? header.strings.sectionSize : header.stringSize;
            var stringAddress = (ulong)stringOffset + index;
            if (stringSize <= 0 || index >= stringSize || stringAddress >= Length)
            {
                result = string.Empty;
                return false;
            }

            try
            {
                result = ReadStringToNull(stringOffset + index);
                stringCache.Add(index, result);
                return true;
            }
            catch (EndOfStreamException)
            {
                result = string.Empty;
                return false;
            }
        }

        public int GetCustomAttributeIndex(Il2CppImageDefinition imageDef, int customAttributeIndex, uint token)
        {
            if (Version > 24)
            {
                if (attributeTypeRangesDic[imageDef].TryGetValue(token, out var index))
                {
                    return index;
                }
                else
                {
                    return -1;
                }
            }
            else
            {
                return customAttributeIndex;
            }
        }

        public string GetStringLiteralFromIndex(uint index)
        {
            if (Version >= 35 && index + 1 >= stringLiterals.Length)
            {
                return string.Empty;
            }
            var stringLiteral = stringLiterals[index];
            var dataOffset = Version >= 38 ? (uint)header.stringLiteralData.offset : header.stringLiteralDataOffset;
            Position = (uint)(dataOffset + stringLiteral.dataIndex);

            int length;
            if (Version <= 31)
            {
                // v31 及之前：length 字段存在
                length = (int)stringLiteral.length;
            }
            else if (Version >= 35)
            {
                // v35+：长度由下一个字符串的 data index 决定
                var nextStringLiteral = stringLiterals[index + 1];
                length = (int)(nextStringLiteral.dataIndex - stringLiteral.dataIndex);
            }
            else
            {
                // v31-v34：需要读取直到 null 终止符
                var currentPos = Position;
                length = 0;
                while (ReadByte() != 0)
                {
                    length++;
                }
                Position = currentPos;
            }

            return Encoding.UTF8.GetString(ReadBytes(length));
        }

        private void ProcessingMetadataUsage()
        {
            metadataUsageDic = new Dictionary<Il2CppMetadataUsage, SortedDictionary<uint, uint>>();
            for (uint i = 1; i <= 6; i++)
            {
                metadataUsageDic[(Il2CppMetadataUsage)i] = new SortedDictionary<uint, uint>();
            }
            foreach (var metadataUsageList in metadataUsageLists)
            {
                for (int i = 0; i < metadataUsageList.count; i++)
                {
                    var offset = metadataUsageList.start + i;
                    if (offset >= metadataUsagePairs.Length)
                    {
                        continue;
                    }
                    var metadataUsagePair = metadataUsagePairs[offset];
                    var usage = GetEncodedIndexType(metadataUsagePair.encodedSourceIndex);
                    var decodedIndex = GetDecodedMethodIndex(metadataUsagePair.encodedSourceIndex);
                    metadataUsageDic[(Il2CppMetadataUsage)usage][metadataUsagePair.destinationIndex] = decodedIndex;
                }
            }
            //metadataUsagesCount = metadataUsagePairs.Max(x => x.destinationIndex) + 1;
            metadataUsagesCount = metadataUsageDic.Max(x => x.Value.Select(y => y.Key).DefaultIfEmpty().Max()) + 1;
        }

        public static uint GetEncodedIndexType(uint index)
        {
            return (index & 0xE0000000) >> 29;
        }

        public uint GetDecodedMethodIndex(uint index)
        {
            if (Version >= 27)
            {
                return (index & 0x1FFFFFFEU) >> 1;
            }
            return index & 0x1FFFFFFFU;
        }

        public int SizeOf(Type type)
        {
            if (Version >= 38)
            {
                if (type == typeof(Il2CppImageDefinition))
                    return 4 + 4 + typeDefinitionIndexSize + 4 + typeDefinitionIndexSize + 4 + methodIndexSize + 4 + 4 + 4;
                if (type == typeof(Il2CppTypeDefinition))
                    return 4 + 4 + typeIndexSize + typeIndexSize + typeIndexSize + genericContainerIndexSize + 4
                        + fieldIndexSize + methodIndexSize + eventIndexSize + propertyIndexSize + nestedTypeIndexSize
                        + interfacesIndexSize + 4 + interfacesIndexSize + 16 + 4 + 4;
                if (type == typeof(Il2CppMethodDefinition))
                    return 4 + typeDefinitionIndexSize + typeIndexSize + 4 + parameterIndexSize + genericContainerIndexSize + 4 + 8;
                if (type == typeof(Il2CppParameterDefinition))
                    return 4 + 4 + typeIndexSize;
                if (type == typeof(Il2CppFieldDefinition))
                    return 4 + typeIndexSize + 4;
                if (type == typeof(Il2CppFieldDefaultValue))
                    return fieldIndexSize + typeIndexSize + defaultValueDataIndexSize;
                if (type == typeof(Il2CppParameterDefaultValue))
                    return parameterIndexSize + typeIndexSize + defaultValueDataIndexSize;
                if (type == typeof(Il2CppPropertyDefinition))
                    return 4 + methodIndexSize + methodIndexSize + 4 + 4;
                if (type == typeof(Il2CppEventDefinition))
                    return 4 + typeIndexSize + methodIndexSize + methodIndexSize + methodIndexSize + 4;
                if (type == typeof(Il2CppGenericContainer))
                    return 12 + genericParameterIndexSize;
                if (type == typeof(Il2CppGenericParameter))
                    return genericContainerIndexSize + 4 + 2 + 2 + 2 + 2;
                if (type == typeof(Il2CppFieldRef))
                    return typeIndexSize + fieldIndexSize;
                if (type == typeof(Il2CppStringLiteral))
                    return Version <= 31 ? 8 : 4;
            }
            var size = 0;
            foreach (var i in type.GetFields())
            {
                var attr = (VersionAttribute)Attribute.GetCustomAttribute(i, typeof(VersionAttribute));
                if (attr != null)
                {
                    if (Version < attr.Min || Version > attr.Max)
                        continue;
                }
                var fieldType = i.FieldType;
                if (fieldType.IsPrimitive)
                {
                    size += GetPrimitiveTypeSize(fieldType.Name);
                }
                else if (fieldType.IsEnum)
                {
                    var e = fieldType.GetField("value__").FieldType;
                    size += GetPrimitiveTypeSize(e.Name);
                }
                else if (fieldType.IsArray)
                {
                    var arrayLengthAttribute = i.GetCustomAttribute<ArrayLengthAttribute>();
                    size += arrayLengthAttribute.Length;
                }
                else
                {
                    size += SizeOf(fieldType);
                }
            }
            return size;

            static int GetPrimitiveTypeSize(string name)
            {
                return name switch
                {
                    "Int32" or "UInt32" => 4,
                    "Int16" or "UInt16" => 2,
                    _ => 0,
                };
            }
        }
    }
}
