// --------------------------------------------------------------------
// Copyright (C) 2003-2010 The Mechanical Frog Project
// http://www.mfcom.ru
// --------------------------------------------------------------------
using System;
using System.Collections;
using System.ComponentModel;

namespace Mfcom.Core.Base
{
	public interface IProtoBackdraft
	{
		bool IsDirty{get;set;}
		void AddRange(ICollection c);
		void RemoveAt(int Index);
	}

	public interface IBackdraft: IProtoBackdraft
	{
		int Add(BaseItem Item);
		void Insert(int Index, BaseItem Value);
	}

	/// <summary>
	/// Коллекции, реализующие этот интерфейс, поддерживают распространение признака "изменено" с содержащихся в коллекции
	/// элементов на саму коллекцию. Это позволяет не тратить время на сканирование коллекции, если в ней нет изменённых
	/// элементов.
	/// </summary>
	public interface IBackdraft<T>: IBackdraft
	{
		int Add(T Item);
		void Insert(int Index, T Value);
	}

	/// <summary>
	/// Стандартная реализация интерфейса IBackdraft. От стандартной отличается тем, что 
	/// об изменениях в хранящихся объектах будет знать и хранящая коллекция
	/// </summary>
	[Serializable]
	public class DefaultBackdraftCollection<T>: BaseCollection<T>, IBackdraft<T> where T : BaseItem, new()
	{
		private readonly UpdateStatusHandler m_Handler;
		private readonly object m_UpdateSync = new object();
		private bool m_IsDirty;

		public DefaultBackdraftCollection()
		{
			m_Handler = InnerItemChanged;
		}

		#region IBackdraft<T> Members

		public bool IsDirty
		{
			get {return m_IsDirty;}
			set
			{
				if (null != BecameDirty)
					BecameDirty(this, new UpdateStatusEventArgs(this, value
					                                                  	? UpdateStatusEventArgs.UpdateState.Set
					                                                  	:
					                                                  		UpdateStatusEventArgs.UpdateState.Reset));
				m_IsDirty = value;
			}
		}

		public new int Add(T Item)
		{
			if (Item == null)
				throw new ArgumentNullException("Item");

			IsDirty = true;
			Item.UpdateStateChanged += m_Handler;
			return base.Add(Item);
		}

		public new void Insert(int Index, T Value)
		{
			IsDirty = true;
			Value.UpdateStateChanged += m_Handler;
			base.Insert(Index, Value);
		}

		public override void AddRange(ICollection c)
		{
			IsDirty = true;
			foreach(BaseItem item in c)
				item.UpdateStateChanged += m_Handler;

			base.AddRange(c);
		}

		public new void RemoveAt(int Index)
		{
			this[Index].UpdateStateChanged -= m_Handler;

			base.RemoveAt(Index);
		}

		int IBackdraft.Add(BaseItem Item)
		{
			return Add((T)Item);
		}

		void IBackdraft.Insert(int Index, BaseItem Value)
		{
			Insert(Index, (T)Value);
		}

		#endregion

		[field: NonSerialized]
		public event UpdateStatusHandler BecameDirty;

		public override void BeforeDelete(int Index)
		{
			IsDirty = true;
			this[Index].UpdateStateChanged -= m_Handler;
		}

		private void InnerItemChanged(object Sender, UpdateStatusEventArgs Args)
		{
			lock(m_UpdateSync)
			{
				if (Args.State == UpdateStatusEventArgs.UpdateState.Set)
					IsDirty = true;
				else if (Args.State == UpdateStatusEventArgs.UpdateState.Reset)
				{
					int c = Count;
					bool dirty = false;
					for(int a = 0; a < c; a++)
						if (this[a].IsModified)
						{
							dirty = true;
							break;
						}

					IsDirty = dirty;
				}
				else
					throw new InvalidEnumArgumentException("Args.State", (int)Args.State, typeof(UpdateStatusEventArgs.UpdateState));
			}
		}
	}
}