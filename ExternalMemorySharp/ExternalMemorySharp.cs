using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ExternalMemory.EmsHelper;

namespace ExternalMemory
{
    public class ExternalMemorySharp
    {
        /// <summary>
        /// Main <see cref="ExternalMemorySharp"/>, It's Used In Zero Param Instance Of <see cref="ExternalClass"/> <para/>
        /// Like (<see cref="ExternalReady.UnrealEngine.TArray{T}"/>, <see cref="ExternalReady.UnrealEngine.FTransform"/>, etc)
        /// </summary>
        public static ExternalMemorySharp MainEms { get; set; }
        public static int MaxStringLen { get; set; } = 64;

        #region [ Delegates ]

        public delegate bool ReadCallBack(UIntPtr address, ulong size, out byte[] bytes);
        public delegate bool WriteCallBack(UIntPtr address, byte[] bytes);

        #endregion

        #region [ Props ]
        public ReadCallBack ReadBytesCallBack { get; }
        public WriteCallBack WriteBytesCallBack { get; }
        public bool Is64BitGame { get; }
        public int PointerSize { get; }
        #endregion

        public ExternalMemorySharp(ReadCallBack readBytesDelegate, WriteCallBack writeBytesDelegate, bool is64BitGame)
        {
            Is64BitGame = is64BitGame;
            PointerSize = Is64BitGame ? 0x8 : 0x4;
            ReadBytesCallBack = readBytesDelegate;
            WriteBytesCallBack = writeBytesDelegate;
        }

        private string ReadString(UIntPtr lpBaseAddress, bool isUnicode = false)
        {
            int charSize = isUnicode ? 2 : 1;
            var ret = new StringBuilder();

            while (true)
            {
                if (!ReadBytes(lpBaseAddress, (uint)charSize, out byte[] buf))
                    break;

                // Null-Terminator
                if (buf.All(b => b == 0x00))
                    break;

                ret.Append(isUnicode ? Encoding.Unicode.GetString(buf) : Encoding.ASCII.GetString(buf));
                lpBaseAddress += charSize;
            }

            return ret.ToString();
        }
        private static void RemoveValueData(IEnumerable<ExternalOffset> unrealOffsets)
        {
            foreach (ExternalOffset unrealOffset in unrealOffsets)
                unrealOffset.RemoveValueAndData();
        }

        public bool ReadBytes(UIntPtr address, uint size, out byte[] bytes)
        {
            bool retState = ReadBytesCallBack(address, size, out bytes);
            // if (!retState)
                // throw new Exception($"Can't read memory at `0x{address.ToInt64():X8}`");

            return retState;
        }
        public bool WriteBytes(UIntPtr address, byte[] bytes)
        {
            return WriteBytesCallBack(address, bytes);
        }

        internal bool ReadClass<T>(T instance, UIntPtr address, byte[] fullClassBytes) where T : ExternalClass
        {
            // Collect Offsets
            List<ExternalOffset> allOffsets = instance.Offsets;

            // Set Bytes
            instance.FullClassBytes = fullClassBytes;

            // Read Offsets
            foreach (ExternalOffset offset in allOffsets)
            {
                #region Checks
                if (offset.Dependency != null && offset.Dependency.OffsetType != OffsetType.UIntPtr && offset.Dependency != ExternalOffset.None)
                    throw new ArgumentException("Dependency can only be pointer (UIntPtr) or 'ExternalOffset.None'");
                #endregion

                #region SetValue
                // if it's Base Offset
                if (offset.Dependency == ExternalOffset.None)
                {
                    offset.SetValueBytes(instance.FullClassBytes);
                    offset.OffsetAddress = address + offset.Offset;
                }
                else if (offset.Dependency != null && offset.Dependency.DataAssigned)
                {
                    offset.SetValueBytes(offset.Dependency.FullClassData);
                    offset.OffsetAddress += offset.Offset;
                }
                // Dependency Is Null-Pointer OR Bad Pointer Then Just Skip
                else if (offset.Dependency != null && (offset.Dependency.OffsetType == OffsetType.UIntPtr && !offset.Dependency.DataAssigned))
                {
                    continue;
                }
                else
                {
                    throw new Exception("Dependency Data Not Set !!");
                }
                #endregion

                #region Init For Dependencies
                // If It's Pointer, Read Pointed Data To Use On Other Offset Dependent On It
                if (offset.OffsetType == OffsetType.UIntPtr)
                {
                    // Get Size Of Pointed Data
                    int pointedSize = Utils.GetDependenciesSize(offset, allOffsets);

                    // If Size Is Zero Then It's Usually Dynamic (Unknown Size) Pointer (Like `Data` Member In `TArray`)
                    // Or Just An Pointer Without Dependencies
                    if (pointedSize == 0)
                        continue;

                    // Set Base Address, So i can set correct address for Dependencies offsets `else if (offset.Dependency.DataAssigned)` UP.
                    // So i just need to add offset to that address
                    offset.OffsetAddress = offset.Read<UIntPtr>();

                    // Can't Read Bytes
                    if (!ReadBytes(offset.Read<UIntPtr>(), (uint)pointedSize, out byte[] dataBytes))
                        continue;

                    offset.SetData(dataBytes);
                }

                // Nested External Class
                else if (offset.OffsetType == OffsetType.ExternalClass)
                {
                    if (offset.ExternalClassIsPointer)
                    {
                        // Get Address Of Nested Class
                        var valPtr = offset.Read<UIntPtr>();

                        // Set Class Info
                        offset.ExternalClassObject.UpdateAddress(valPtr);

                        // Null Pointer
                        if (valPtr != UIntPtr.Zero)
                        {
	                        // Read Nested Pointer Class
	                        if (!ReadClass(offset.ExternalClassObject, valPtr))
	                        {
		                        // throw new Exception($"Can't Read `{offset.ExternalClassType.Name}` As `ExternalClass`.", new Exception($"Value Count = {offset.Size}"));
		                        return false;
	                        }
                        }
                    }
                    else
                    {
                        UIntPtr nestedAddress = address + offset.Offset;

                        // Set Class Info
                        offset.ExternalClassObject.UpdateAddress(nestedAddress);

                        // Read Nested Instance Class
                        if (!ReadClass(offset.ExternalClassObject, nestedAddress, (byte[])offset.Value))
                        {
                            // throw new Exception($"Can't Read `{offset.ExternalClassType.Name}` As `ExternalClass`.", new Exception($"Value Count = {offset.Size}"));
                            return false;
                        }
                    }
                }
                #endregion
            }

            return true;
        }
        public bool ReadClass<T>(T instance, UIntPtr address) where T : ExternalClass
        {
            if (address.ToUInt64() <= 0)
            {
                // Clear All Class Offset
                RemoveValueData(instance.Offsets);
                return false;
            }

            // Read Full Class
            if (ReadBytes(address, (uint)instance.ClassSize, out byte[] fullClassBytes))
                return ReadClass(instance, address, fullClassBytes);

            // Clear All Class Offset
            RemoveValueData(instance.Offsets);
            return false;
        }

        public bool ReadClass<T>(T instance, int address) where T : ExternalClass => ReadClass(instance, (UIntPtr)address);
        public bool ReadClass<T>(T instance, long address) where T : ExternalClass => ReadClass(instance, (UIntPtr)address);
    }
}
