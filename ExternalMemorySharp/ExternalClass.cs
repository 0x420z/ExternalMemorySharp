using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExternalMemory.EmsHelper;

namespace ExternalMemory
{
    public abstract class ExternalClass
    {
        public ExternalMemorySharp Ems { get; internal set; }
        internal List<ExternalOffset> Offsets { get; }
        internal int ClassSize { get; set; }
        internal byte[] FullClassBytes { get; set; }

        public UIntPtr BaseAddress { get; private set; }

        protected ExternalClass(ExternalMemorySharp emsInstance, UIntPtr address)
        {
            Ems = emsInstance ?? throw new NullReferenceException("emsInstance Can't be null, Use 'ExternalMemorySharp.MainEms' instead.");
            BaseAddress = address;

            // ReSharper disable once VirtualMemberCallInConstructor
            InitOffsets();

            // Get All Offset Props
            Offsets = this.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(ExternalOffset) || f.FieldType.IsSubclassOfRawGeneric(typeof(ExternalOffset<>)))
                .Select(f =>
                {
                    var curOffset = (ExternalOffset)f.GetValue(this);

                    // Set Info
                    curOffset.Name = f.Name;
                    curOffset.Ems = Ems;

                    // If It's 32bit Game Then Pointer Is 4Byte
                    if (curOffset.OffsetType == OffsetType.UIntPtr && !curOffset.IsGame64Bit)
	                    curOffset.Size = 0x4;
                    else if (curOffset.OffsetType == OffsetType.ExternalClass && curOffset.ExternalClassIsPointer)
	                    curOffset.Size = curOffset.IsGame64Bit ? 0x8 : 0x4;

                    return curOffset;
                })
                .ToList();

            // Re order Offsets
            List<ExternalOffset> offsetsList = Offsets
	            .Where(f => f != null)
	            .ToList();

            // Sort By Dependencies
            offsetsList = offsetsList.OrderBy(off => off.Offset).ToList();
            List<ExternalOffset> offsets = offsetsList;
            Offsets = offsetsList.Sort(off => offsets.Where(offset => offset == off.Dependency));

            // Get Size Of Class
            ClassSize = Utils.GetDependenciesSize(ExternalOffset.None, Offsets);
        }

        /// <summary>
        /// Override this function to init Offsets Of Your Class
        /// </summary>
        protected virtual void InitOffsets() {}

        /// <summary>
        /// Update <see cref="BaseAddress"/> Of This Class
        /// </summary>
        /// <param name="newAddress"></param>
        public virtual void UpdateAddress(UIntPtr newAddress)
        {
            BaseAddress = newAddress;
        }

        /// <summary>
        /// Update Memory Reader/Writer
        /// </summary>
        /// <param name="externalMemorySharp">New External Memory Sharp Class That's Will Used To Read And Write</param>
        public void UpdateReader(ExternalMemorySharp externalMemorySharp)
        {
            Ems = externalMemorySharp;
        }

        /// <summary>
        /// Read Data And Set It On This Class
        /// </summary>
        public virtual bool UpdateData()
        {
	        return BaseAddress != UIntPtr.Zero && Ems.ReadClass(this, BaseAddress);
        }

        /// <summary>
        /// Set Data On This Class (Doesn't Call ReadMemory Callback) <para />
        /// Be Careful When Using This Function
        /// </summary>
        /// <param name="fullClassBytes">Full <see cref="ExternalClass"/> Bytes</param>
        public virtual bool UpdateData(byte[] fullClassBytes)
        {
            return BaseAddress != UIntPtr.Zero && Ems.ReadClass(this, BaseAddress, fullClassBytes);
        }
    }
}
