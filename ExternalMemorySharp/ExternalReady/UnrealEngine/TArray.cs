﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ExternalMemory.ExternalReady.UnrealEngine
{
    // ReSharper disable once InconsistentNaming
    /// <summary>
    /// TArray Class To Fit UnrealEngine, It's only Support Pointer To <see cref="ExternalClass"/> Only
    /// </summary>
    public class TArray<T> : ExternalClass where T : ExternalClass, new()
    {
        public class DelayData
        {
            public int DelayEvery { get; set; } = 1;
            public int Delay { get; set; } = 0;
        }
        public class ReadData
        {
            public bool IsPointer { get; set; } = true;
            public int BadSizeAfterEveryItem { get; set; } = 0x0;
            public bool UseMaxAsReadCount { get; set; } = false;
        }

        public List<T> Items { get; } = new List<T>();
        private readonly int _itemSize;

        #region Offsets
        protected ExternalOffset<IntPtr> _data;
        protected ExternalOffset<int> _count;
        protected ExternalOffset<int> _max;
        #endregion

        #region Props
        public int MaxCountTArrayCanCarry { get; } = 0x20000;
        public DelayData DelayInfo { get; } = new DelayData();
        public ReadData ReadInfo { get; } = new ReadData();

        public IntPtr Data => _data.GetValue<IntPtr>();
        public int Count => _count.GetValue<int>();
        public int Max => _max.GetValue<int>();
        #endregion

        public TArray(ExternalMemorySharp emsInstance, IntPtr address) : base(emsInstance, address)
        {
            _itemSize = ((T)Activator.CreateInstance(typeof(T), Ems, (IntPtr)0x0)).ClassSize;
        }

        /// <summary>
        /// Just use this constract for pass this class as Genric Param <para/>
        /// Will Use <see cref="ExternalMemorySharp.MainEms"/> As Reader<para />
        /// You Can call '<see cref="ExternalClass.UpdateReader(ExternalMemorySharp)"/> To Override
        /// </summary>
        public TArray() : this(ExternalMemorySharp.MainEms, IntPtr.Zero) {}

        public TArray(ExternalMemorySharp emsInstance, IntPtr address, int maxCountTArrayCanCarry) : this(emsInstance, address)
        {
            MaxCountTArrayCanCarry = maxCountTArrayCanCarry;
        }

        protected override void InitOffsets()
        {
	        base.InitOffsets();

            int curOff = 0x0;
            _data = new ExternalOffset<IntPtr>(ExternalOffset.None, curOff); curOff += Ems.Is64BitGame ? 0x8 : 0x4;
            _count = new ExternalOffset<int>(ExternalOffset.None, curOff); curOff += 0x4;
            _max = new ExternalOffset<int>(ExternalOffset.None, curOff);
        }

        public override bool UpdateData()
        {
            // Read Array (Base and Size)
            if (!Read())
                return false;

            int counter = 0;
            int itemSize = ReadInfo.IsPointer ? (Ems.Is64BitGame ? 8 : 4) : _itemSize;
            itemSize += ReadInfo.BadSizeAfterEveryItem;

            // Get TArray Data
            Ems.ReadBytes(Data, Items.Count * itemSize, out byte[] tArrayData);
            var bytes = new List<byte>(tArrayData);

            int offset = 0;
            foreach (T item in Items)
            {
                IntPtr itemAddress;
                if (ReadInfo.IsPointer)
				{
                    // Get Item Address (Pointer Value (aka Pointed Address))
                    itemAddress = Ems.Is64BitGame
                        ? (IntPtr)BitConverter.ToUInt64(tArrayData, offset)
                        : (IntPtr)BitConverter.ToUInt32(tArrayData, offset);
                }
                else
				{
                    itemAddress = this.BaseAddress + offset;
                }

                // Update current item
                item.UpdateAddress(itemAddress);

                // Set Data
                if (ReadInfo.IsPointer)
                    item.UpdateData();
                else
                    item.UpdateData(bytes.GetRange(offset, itemSize).ToArray());

                // Move Offset
                offset += itemSize;

                if (DelayInfo.Delay == 0)
	                continue;

                counter++;
                if (counter < DelayInfo.DelayEvery)
	                continue;

                Thread.Sleep(DelayInfo.Delay);
                counter = 0;
            }

            return true;
        }
        private bool Read()
        {
            if (Ems == null)
                throw new NullReferenceException($"Ems is null, Are u miss calling 'UpdateReader` Or Set 'MainEms' !!");

            if (!Ems.ReadClass(this, BaseAddress))
                return false;

            int count = ReadInfo.UseMaxAsReadCount ? Max : Count;

            if (count > MaxCountTArrayCanCarry)
                return false;

            // TODO: Change This Logic
            try
            {
                if (Items.Count > count)
                {
                    Items.RemoveRange(count, Items.Count - count);
                }
                else if (Items.Count < count)
                {
                    Enumerable.Range(Items.Count, count).ToList().ForEach(num =>
                    {
                        T instance = (T)Activator.CreateInstance(typeof(T), Ems, (IntPtr)0x0);
                        Items.Add(instance);
                    });
                }
            }
            catch
            {
                return false;
            }

            return true;
        }
        public bool IsValid()
        {
	        int count = ReadInfo.UseMaxAsReadCount ? Max : Count;

            if (count == 0 && !Read())
                return false;

            return (Max > Count) && BaseAddress != IntPtr.Zero;
        }

        #region Indexer
        public T this[int index] => Items[index];
        #endregion
    }
}
