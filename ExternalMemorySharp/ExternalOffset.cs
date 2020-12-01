using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using ExternalMemory.EmsHelper;

namespace ExternalMemory
{
    public enum OffsetType
    {
        None,
        Custom,

        /// <summary>
        /// DON'T USE, It's For <see cref="ExternalOffset{T}"/> Only
        /// </summary>
        ExternalClass,

        UIntPtr,
        String
    }

    public abstract class ExternalOffset
    {
        public static ExternalOffset None { get; } = new ExternalOffset<byte>(null, 0x0, OffsetType.None);

        #region [ Proparites ]

        /// <summary>
        /// MemoryReader Used To Read This Offset
        /// </summary>
        internal ExternalMemorySharp Ems { get; set; }

        internal UIntPtr OffsetAddress { get; set; }
        public ExternalOffset Dependency { get; protected set; }
        public int Offset { get; protected set; }
        public OffsetType OffsetType { get; protected set; }
        protected MarshalType OffsetMarshalType { get; set; }

        #region [ GenricExternalClass ]
         
        /// <summary>
        /// DON'T USE, IT FOR `<see cref="ExternalOffset{T}"/>` And `<see cref="OffsetType"/>.ExternalClass` Only
        /// </summary>
        internal Type ExternalClassType { get; set; }

        /// <summary>
        /// DON'T USE, IT FOR `<see cref="ExternalOffset{T}"/>` And `<see cref="OffsetType"/>.ExternalClass` Only
        /// </summary>
        internal ExternalClass ExternalClassObject { get; set; }

        /// <summary>
        /// DON'T USE, IT FOR `<see cref="ExternalOffset{T}"/>` And `<see cref="OffsetType"/>.ExternalClass` Only
        /// </summary>
        internal bool ExternalClassIsPointer { get; set; }
        #endregion

        /// <summary>
        /// Offset Name
        /// </summary>
        internal string Name { get; set; }

        /// <summary>
        /// Offset Size
        /// </summary>
        internal int Size { get; set; }

        /// <summary>
        /// Offset Value As Object
        /// </summary>
        internal object Value { get; set; }

        /// <summary>
        /// If Offset Is Pointer Then We Need A Place To Store
        /// Data It's Point To
        /// </summary>
        internal byte[] FullClassData { get; set; }

        internal bool DataAssigned { get; private set; }
        internal bool IsGame64Bit => Ems?.Is64BitGame ?? false;

        #endregion

        internal T Read<T>()
        {
            Type tType = typeof(T);
            if (OffsetType == OffsetType.ExternalClass && tType != typeof(UIntPtr) && tType != typeof(IntPtr))
                return (T)(object)ExternalClassObject;

            if (Value == null)
                return default;

            return (T)Value;
        }
        protected bool Write<T>(T value)
        {
            if (OffsetAddress == UIntPtr.Zero)
                return false;

            Value = value;
            return Ems.WriteBytes(OffsetAddress, OffsetMarshalType.ObjectToByteArray(Value));
        }

        internal void SetValueBytes(byte[] fullDependencyBytes)
        {
            var valueBytes = new byte[Size];

            // (Dependency == None) Mean it's Base Class Data
            Array.Copy(Dependency == None ? fullDependencyBytes : Dependency.FullClassData, Offset, valueBytes, 0, valueBytes.Length);

            // Set Value
            switch (OffsetType)
            {
                case OffsetType.String:
                    Value = GetStringFromBytes(fullDependencyBytes, true);
                    break;
                case OffsetType.UIntPtr:
                case OffsetType.ExternalClass when ExternalClassIsPointer:
                    Value = (UIntPtr)(IsGame64Bit ? valueBytes.ToStructure<ulong>() : valueBytes.ToStructure<uint>());
                    break;
                case OffsetType.ExternalClass:
                    // The value is ByteArray it's for Offsets inside the ExternalClass
                    Value = valueBytes;
                    break;
                default:
                    Value = OffsetMarshalType.ByteArrayToObject(valueBytes);
                    break;
            }
        }
        internal void SetData(byte[] bytes)
        {
            DataAssigned = true;
            FullClassData = bytes;
        }
        internal void RemoveValueAndData()
        {
            DataAssigned = false;

            // ToDo: Change This Logic
            if (Value != null)
                Value = ExternalClassType == null ? default : Activator.CreateInstance(ExternalClassType);

            if (FullClassData != null)
                Array.Clear(FullClassData, 0, FullClassData.Length);
        }

        #region [ String ]

        // ToDO: this function case memory leak, Fix IT
        internal string GetStringFromBytes(byte[] fullDependencyBytes, bool isUnicode)
        {
            int len = fullDependencyBytes.Length > (Offset + ExternalMemorySharp.MaxStringLen)
                ? ExternalMemorySharp.MaxStringLen
                : fullDependencyBytes.Length - Offset;
            var buf = new byte[len];

            Array.Copy(fullDependencyBytes, Offset, buf, 0, buf.Length);
            return Utils.BytesToString(buf, isUnicode).Split('\0')[0];
        }

        #endregion
    }

    public sealed class ExternalOffset<T> : ExternalOffset
    {
        public ExternalOffset(int offset) : this(None, offset) {}
        internal ExternalOffset(int offset, OffsetType offsetType) : this(None, offset, offsetType) { }
        public ExternalOffset(int offset, bool externalClassIsPointer) : this(None, offset, externalClassIsPointer) {}

        /// <summary>
        /// For Init Custom Types Like (<see cref="UIntPtr"/>, <see cref="int"/>, <see cref="float"/>, <see cref="string"/>, ..etc)
        /// </summary>
        public ExternalOffset(ExternalOffset dependency, int offset) : this(dependency, offset, OffsetType.Custom)
        {
	        if (typeof(T).IsSubclassOf(typeof(ExternalClass)))
		        throw new InvalidCastException("Use Other Constructor For `ExternalClass` Types.");

            Init();
        }

        /// <summary>
        /// For Init <see cref="ExternalClass"/>
        /// </summary>
        public ExternalOffset(ExternalOffset dependency, int offset, bool externalClassIsPointer) : this(dependency, offset, OffsetType.ExternalClass)
        {
            if (!typeof(T).IsSubclassOf(typeof(ExternalClass)))
                throw new InvalidCastException("This Constructor For `ExternalClass` Types Only.");

            ExternalClassType = typeof(T);
            ExternalClassIsPointer = externalClassIsPointer;
            ExternalClassObject = (ExternalClass)Activator.CreateInstance(ExternalClassType);

            Init();
        }

        /// <summary>
        /// Main
        /// </summary>
        internal ExternalOffset(ExternalOffset dependency, int offset, OffsetType offsetType)
        {
	        Dependency = dependency;
	        Offset = offset;
	        OffsetType = offsetType;
        }

        private void Init()
        {
	        Type thisType = typeof(T);
	        if (thisType == typeof(string))
	        {
		        OffsetType = OffsetType.String;
		        OffsetMarshalType = new MarshalType(typeof(UIntPtr));
				Size = OffsetMarshalType.Size;
            }
            else if (thisType == typeof(IntPtr) || thisType == typeof(UIntPtr))
            {
                OffsetType = OffsetType.UIntPtr;
                OffsetMarshalType = new MarshalType(typeof(UIntPtr));
				Size = OffsetMarshalType.Size;
            }
            else if (thisType.IsSubclassOf(typeof(ExternalClass)) || thisType.IsSubclassOfRawGeneric(typeof(ExternalOffset<>)))
            {
                // OffsetType Set On Other Constructor If It's `ExternalClass`
                if (OffsetType != OffsetType.ExternalClass)
                    throw new Exception("Use Other Constructor For `ExternalClass` Types.");

                // If externalClassIsPointer == true, ExternalClass Will Fix The Size Before Calc Class Size
                // So It's Okay To Leave It Like That
                Size = ExternalClassObject.ClassSize;

                OffsetMarshalType = ExternalClassIsPointer ? new MarshalType(typeof(UIntPtr)) : null;
            }
            else
			{
				Size = Marshal.SizeOf<T>();
				OffsetMarshalType = new MarshalType(thisType);
            }
        }

        public T Read() => Read<T>();
        public bool Write(T value) => Write<T>(value);
    }
}
