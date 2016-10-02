// --------------------------------------------------------------------
// Copyright (C) 2003-2010 The Mechanical Frog Project
// http://www.mfcom.ru
// --------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using Mfcom.Core.DataAccess;
using Mfcom.Core.Utils;
using Mfcom.Core.Utils.Exceptions;
using Mfcom.Replication;

namespace Mfcom.Core.Base
{
	/// <summary>
	/// Интерфейс должен реализовываться для коллекций, обратно прикрепляемых к ведущим объектам.
	/// </summary>
	public interface IHasParents
	{
		long ParentId{set;}
	}

	internal class CollectionEnumerator<T>: IEnumerator<T> where T : BaseItem, new()
	{
		private readonly BaseCollection<T> m_Collection;
		private int m_Current;

		public CollectionEnumerator(BaseCollection<T> Collection)
		{
			m_Collection = Collection;
			m_Current = -1;
		}

		#region IEnumerator<T> Members

		public bool MoveNext()
		{
			m_Current += 1;
			if (m_Current < m_Collection.Count)
				return true;

			return false;
		}

		object IEnumerator.Current
		{
			get {return m_Collection[m_Current];}
		}

		public T Current
		{
			get
			{
				if (m_Current == -1)
					throw new InvalidOperationException("Current");

				return m_Collection[m_Current];
			}
		}

		public void Reset()
		{
			m_Current = -1;
		}

		public void Dispose()
		{
		}

		#endregion
	}

	public interface ICollectionMarker: IEnumerable
	{
		int Count{get;}
		object this[int Index]{get;}
	}

	public enum UpdateResultCode
	{
		None = -1,
		Ok = 0
	}

	public interface IECCCollection
	{
		BaseItem FindById(long Id);
		void DeleteById(long Id);
		void Delete(int Index);
		UpdateResultCode Update();
		int Add(BaseItem Item);
	}

	public interface IECCCollection<T>: IECCCollection
	{
		new T FindById(long Id);
	}

	public interface INavigable: ICollectionMarker
	{
		BaseItem First{get;}
		BaseItem Last{get;}
		bool Empty{get;}
	}

	public interface INavigable<T>: ICollectionMarker
	{
		T First{get;}
		T Last{get;}
		bool Empty{get;}
	}

	/// <summary>
	/// Предок всех коллекций. Содержит методы для сохранения объектов, изменённых пользователем
	/// в процессе работы с коллекцией
	/// </summary>
	[Serializable]
	public partial class BaseCollection<T>: BindingList<T>,
	                                        IEnumerable<T>,
	                                        ICollection,
	                                        IECCCollection<T>,
	                                        INavigable<T>,
	                                        INavigable
		where T : BaseItem, new()
	{
		private readonly object m_SyncRoot = new object();
		[NonSerialized] protected IDataStorageAdapter m_Adapter;

		/// <summary>
		/// Устанавливает/возвращает объект по его индексу с учётом удалённых объектов. Те объекты, которые помечены удалёнными,
		/// данным индексатором не возвращаются. Базовый индекс - 0.
		/// </summary>
		public new T this[int Index]
		{
			get
			{
				Index = CalculateRawIndex(Index);

				if (Index < 0 || Index >= RawCount)
					return null;

				lock(SyncRoot)
					return base[Index];
			}
			set
			{
				Index = CalculateRawIndex(Index);

				if (Index >= 0 && Index < RawCount)
					lock(SyncRoot)
						base[Index] = value;
			}
		}

		/// <summary>
		/// Число физических элементов в хранилище
		/// </summary>
		public int RawCount
		{
			get
			{
				lock(SyncRoot)
					return base.Count;
			}
		}

		#region ICollection Members

		int ICollection.Count
		{
			get {return Count;}
		}

		public bool IsSynchronized
		{
			get {return true;}
		}

		public object SyncRoot
		{
			get {return m_SyncRoot;}
		}

		#endregion

		#region IECCCollection<T> Members

		/// <summary>
		/// Помечает элемент как удалённый, не удаляя его из коллекции
		/// </summary>
		/// <param name="Index">Индекс помечаемого элемента</param>
		public void Delete(int Index)
		{
			T item = this[Index];

			BeforeDelete(Index);
			item.IsDeleted = true;
		}

		/// <summary>
		/// В зависимости от значений служебных полей класса-потомка BaseItem сохраняет, удаляет 
		/// или изменяет соответствующий объект во внешнем хранилище
		/// </summary>
		/// <returns>
		/// Возвращает SUCCESS_CODE если все элементы в коллекции удалось обработать
		/// В случае некритической ошибки возвращает номер элемента коллекции, на котором
		/// произошёл сбой. Ретранслирует вызвавшему все исключения, возникшие в процессе работы
		/// метода. В том числе и исключения библиотеки хранилища, напр., ADO
		/// </returns>
		/// <remarks>SUCCESS_CODE == -1</remarks>
		public UpdateResultCode Update()
		{
			if (this is IBackdraft)
			{
				var ou = this as IBackdraft;
				if (!ou.IsDirty)
					return UpdateResultCode.Ok;
			}

#if DEBUG
			bool wasInTrans = m_Adapter.IsInTransaction();
#endif

			try
			{
				int index = 0;
#if DEBUG
				Debug.Assert(m_Adapter != null, String.Format("Адаптер не проинициализирован для типа: {0}", GetType().FullName));
#endif
				if (!BaseSolutionProperties.UserManagedTransactions)
					m_Adapter.BeginTransaction();

				while(true)
				{
					if (index >= RawCount)
						break;

					T element;

					lock(SyncRoot)
						element = base[index];
					// Вне зависимости от того, что реально требуется сделать с объектом - удалить ли, сохранить ли, или обновить,
					// не делать ничего
					if (element.DeferDBOperations)
					{
						index += 1;
						continue;
					}

					SqlAction action = element.GetOperationType();
					switch(action)
					{
						case SqlAction.Insert: //BugzID: 600
							CreateItemInTrans(CalculateSmartIndex(index++));
							element.MakeIntact();
							break;

						case SqlAction.Update: //BugzID: 600
							UpdateItemInTrans(CalculateSmartIndex(index++));
							element.MakeIntact();
							break;

						case SqlAction.Delete:
							DeleteItemInTrans(RawAt(index).Id);
							RemoveAt(index);
							break;

						case SqlAction.Nothing:
							index += 1;
							break;

						default:
							throw new InvalidOperationException("Action flag is Unknown");
					}
				}

				return UpdateResultCode.Ok;
			}
			finally
			{
				if (!BaseSolutionProperties.UserManagedTransactions)
					m_Adapter.EndTransaction();

#if DEBUG
				if (!wasInTrans && m_Adapter.IsInTransaction())
                    Logger.WriteError(new Exception("Connection is in transaction after end of update. UserName - " + Environment.UserDomainName));
#endif
			}
		}

		T IECCCollection<T>.FindById(long Id)
		{
			return FindById(Id);
		}

		BaseItem IECCCollection.FindById(long Id)
		{
			return FindById(Id);
		}

		void IECCCollection.DeleteById(long Id)
		{
			DeleteById(Id);
		}

		int IECCCollection.Add(BaseItem Item)
		{
			return Add((T)Item);
		}

		#endregion

		/* IEnumerable<T> */

		#region IEnumerable<T> Members

		public new IEnumerator<T> GetEnumerator()
		{
			return new CollectionEnumerator<T>(this);
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return new CollectionEnumerator<T>(this);
		}

		#endregion

		#region INavigable Members

		BaseItem INavigable.First
		{
			get {return First;}
		}

		BaseItem INavigable.Last
		{
			get {return Last;}
		}

		#endregion

		#region INavigable<T> Members

		/// <summary>
		/// Количество элементов в коллекции. Элементы, отмеченные удалёнными - не учитываются
		/// </summary>
		public new int Count
		{
			get
			{
				lock(SyncRoot)
				{
					int c = 0;
					int baseCount = base.Count;
					for(int a = 0; a < baseCount; a++)
						if (base[a].IsDeleted)
							c += 1;

					return baseCount - c;
				}
			}
		}

		object ICollectionMarker.this[int Index]
		{
			get
			{
				Index = CalculateRawIndex(Index);
				if (Index < 0 || Index >= RawCount)
					return null;

				lock(SyncRoot)
					return base[Index];
			}
		}

		/// <summary>
		/// Последний элемент коллекции
		/// </summary>
		public T Last
		{
			get
			{
				if (Count != 0)
					return this[Count - 1];

				throw new IndexOutOfRangeException("Collection contains ZERO elements");
			}
		}

		/// <summary>
		/// Первый элемент коллекции
		/// </summary>
		public T First
		{
			get {return this[0];}
		}

		public bool Empty
		{
			get {return Count == 0;}
		}

		#endregion

		/// <summary>
		/// Вычисляет требуемый "сырой" индекс по "умному", то есть, по логическому индексу
		/// вычисляется физический с учётом отмеченных к удалению объектов
		/// </summary>
		/// <param name="Index">Логический индекс</param>
		/// <returns>Физический (сырой) индекс с учётом пропусков удалённых объектов</returns>
		private int CalculateRawIndex(int Index)
		{
			if (Index >= Count || Index < 0)
				return -1;

			int rawIndex = 0,
			    smartIndex = -1;

			lock(SyncRoot)
				do
				{
					if (!(base[rawIndex]).IsDeleted)
						smartIndex += 1;

					rawIndex += 1;
				} while(smartIndex != Index && rawIndex < RawCount);

			if (smartIndex == Index)
				return --rawIndex;

			return -1;
		}

		// BugzID: 600
		private int CalculateSmartIndex(int RawIndex)
		{
			if (RawIndex >= RawCount || RawIndex < 0)
				return -1;

			int smart = -1;
			lock(SyncRoot)
				for(int a = 0; a <= RawIndex; a++)
					if (!base[a].IsDeleted)
						smart += 1;

			return smart;
		}

		public event AddingNewEventHandler AddingNew2;

		/// <summary>
		/// Добавить объект в хвост коллекции
		/// </summary>
		/// <param name="Value">Объект</param>
		/// <returns>Индекс, который имеет объект в коллекции после вставки</returns>
		public new int Add(T Value)
		{
			lock(SyncRoot)
			{
				base.Add(Value);

				if (AddingNew2 != null)
					AddingNew2(this, new AddingNewEventArgs(Value));

				return base.Count;
			}
		}

		/// <summary>
		/// Встатить элемент в коллекцию
		/// </summary>
		/// <param name="Index">Индекс вставки</param>
		/// <param name="Value">Объект, подлежащий вставке</param>
		public new void Insert(int Index, T Value)
		{
			lock(SyncRoot)
				base.Insert(Index, Value);
		}

		public new bool Contains(T value)
		{
			lock(SyncRoot)
				return base.Contains(value);
		}

		/// <summary>
		/// Добавить в коллекцию несколько элементов за один вызов
		/// </summary>
		/// <param name="c"></param>
		public virtual void AddRange(ICollection c)
		{
			lock(SyncRoot)
			{
				foreach(T t in c)
					base.Add(t);
			}
		}

		/// <summary>
		/// Необходима для потомка BackdraftCollection для оповещения родительской коллекции об удалении элемента
		/// </summary>
		public virtual void BeforeDelete(int Index)
		{
		}

		/// <summary>
		/// Помечает все элемены в коллекции удалёнными
		/// </summary>
		public void DeleteAll()
		{
			lock(SyncRoot)
			{
				int cnt = Count; // Количество неудалённых до начала удаления. 
				// Использовать здесь RawCount нецелесообразно, так как он повторно пройдётся и по удалённым
				for(int a = 0; a < cnt; a++) // BugzID: 920
					Delete(0);
			}
		}

		/// <summary>
		/// Находит элемент с равным id в коллекции и заменяет его элементом value.
		/// Возвращает индекс элемента.
		/// Например может применяться для установки измененного клона элемента вместо оригинального элемента.
		/// </summary>
		/// <param name="Value"></param>
		public int SetItem(T Value)
		{
			int Index = FindIndexById(Value.Id);
			base[Index] = Value;
			return Index;
		}

		/// <summary>
		/// Находит элемент с заданным индексом в коллекции и заменяет его элементом value.
		/// Например может применяться для установки измененного клона элемента вместо оригинального элемента.
		/// </summary>
		/// <param name="Value"></param>
		protected void SetItem(int Index, T Value, bool Dummy)
		{
			base[Index] = Value;
		}

		/// <summary>
		/// Вызывается, когда необходимо удалить элемент из хранилища.
		/// </summary>
		/// <param name="Id">Уникальный номер объекта (Id)</param>
		/// <returns>В случае успеха возвращает true, иначе - false</returns>
		/// <remarks>Id - ключ объекта в БД</remarks>
		protected virtual bool DeleteItem(long Id)
		{
			return m_Adapter.Delete(Id) > 0;
		}

		/// <summary>
		/// Вызывается, когда необходимо удалить элемент из хранилища.
		/// </summary>
		/// <param name="Id">Уникальный номер объекта (Id)</param>
		/// <returns>В случае успеха возвращает true, иначе - false</returns>
		/// <remarks>Id - ключ объекта в БД</remarks>
		private bool DeleteItemInTrans(long Id)
		{
			bool Result = true;

//			m_Adapter.BeginTransaction();

			try
			{
				// Метод может возвратить false если не было обработано ни одной строки - 
				// например, не подошли под условие запроса на удаление.

				if (!(Result = DeleteItem(Id)))
					throw new Exception("item deletion failed");

//				m_Adapter.EndTransaction();
			}
			catch(DatabaseException)
			{
				m_Adapter.RollBack();
				throw;
			}
			catch(Exception e)
			{
				m_Adapter.RollBack();

				if (Result)
					ReportError(e, "удаления");
				else
					throw new UserException("Произошла логическая ошибка при записи в БД!", e);
			}

			return Result;
		}

		private void ReportError(Exception e, string Action)
		{
			string DeveloperError;

			if (e is DatabaseException)
				DeveloperError = e.InnerException.Message;
			else if (e is UserException)
				DeveloperError = e.Message;
			else
				DeveloperError = String.Format("Объект {0} не был сохранен в БД.", GetType().Name);

			throw new UserException(
				String.Format("Произошла ошибка при попытке {0} объекта в БД!", Action),
				DeveloperError);
		}

		/// <summary>
		/// Абстрактный метод. Вызывается, когда необходимо обновить элемент в хранилище.
		/// </summary>
		/// <param name="Index">Порядковый номер элемента в коллекции</param>
		/// <returns>В случае успеха возвращает true, иначе - false</returns>
		protected virtual bool UpdateItem(int Index)
		{
			return false;
		}

		/// <summary>
		/// Внутренний метод, обеспечивающий транзакционность при обновлении сложных объектов.
		/// Вызывается, когда необходимо обновить элемент в хранилище.
		/// </summary>
		/// <param name="Index">Порядковый номер элемента в коллекции</param>
		/// <returns>В случае успеха возвращает true, иначе - false</returns>
		private bool UpdateItemInTrans(int Index)
		{
			bool result = true;

			try
			{
				if (!(result = UpdateItem(Index)))
					throw new Exception("item updating failed");
			}
			catch(DatabaseException de)
			{
				CommonReportError(de, result);
			}
			catch(Exception e)
			{
				CommonReportError(e, result);
			}

			return result;
		}

		/// <summary>
		/// Абстрактный метод. Вызывается, когда необходимо создать элемент в хранилище.
		/// </summary>
		/// <param name="Index">Порядковый номер элемента в коллекции</param>
		/// <returns>В случае успеха возвращает true, иначе - false</returns>
		protected virtual bool CreateItem(int Index)
		{
			return false;
		}

		/// <summary>
		/// Внутренний метод, обеспечивающий транзакционность при создании сложных объектов.
		/// Вызывается, когда необходимо создать элемент в хранилище.
		/// </summary>
		/// <param name="Index">Порядковый номер элемента в коллекции</param>
		/// <returns>В случае успеха возвращает true, иначе - false</returns>
		private bool CreateItemInTrans(int Index)
		{
			bool result = true;

			try
			{
				if (!(result = CreateItem(Index)))
					throw new Exception("item insertion failed");
			}
			catch(Exception e)
			{
				CommonReportError(e, result);
			}

			return result;
		}

		private void CommonReportError(Exception e, bool result)
		{
			m_Adapter.RollBack();

			string pattern = "Произошла {0}ошибка при {1}записи {2}в БД: {3}";

			if (result)
			{
				string developerMsg;

				if (e is DatabaseException)
					developerMsg = e.InnerException.Message;
				else if (e is UserException)
					developerMsg = e.Message;
				else
					developerMsg = String.Format("Объект {0} не был сохранен в БД.", GetType().Name);

				string msg = String.Format(pattern, "", "попытке ", "объекта ", e.Message);

				throw new UserException(msg, developerMsg);
			}
			else
			{
				string msg = String.Format(pattern, "логическая ", "", "", e.Message);

				throw new UserException(msg, e);
			}
		}

		/// <summary>
		/// Получить элемент из коллекции по его идентификатору(ключу), записанному в таблицу
		/// </summary>
		/// <param name="Id">Значение идентификатора(ключа)</param>
		/// <returns>Возвращает найденный объект, приведённый к типу BaseItem или возвращает null, если ничего не удалось найти</returns>
		public virtual T FindById(long Id)
		{
			for(int i = 0; i < Count; i++)
				if (this[i].Id == Id)
					return this[i];

			return null;
		}

		public int FindIndexById(long Id)
		{
			for(int i = 0; i < Count; i++)
				if (this[i].Id == Id)
					return i;

			return -1;
		}

		/// <summary>
		/// Удалить элемент из коллекции по Id
		/// </summary>
		/// <param name="Id"></param>
		/// <returns></returns>
		public void DeleteById(long Id)
		{
			int n = -1;

			for(int i = 0; i < Count; i++)
				if (this[i].Id == Id)
				{
					n = i;
					break;
				}

			if (n == -1)
				throw new UserException(String.Format("Не найден Id удаляемого объекта. Id = {0}", Id),
				                        GetType().ToString(),
				                        "Method can't be defined.");
			else
				Delete(n);
		}

		public virtual bool CheckDuplicate()
		{
			return false;
		}

		/// <summary>
		/// Возвращает элемент по его "сырому" индексу, без учёта элементов,
		/// помеченных, как удалённые
		/// </summary>
		/// <param name="Index">Сырой индекс элемента</param>
		/// <returns>Объект, соответствующий индексу или исключение, если индекс не 
		/// является действительным</returns>
		public T RawAt(int Index)
		{
			if (Index < 0 || Index >= RawCount)
				throw new IndexOutOfRangeException("index is bad");

			lock(this)
				return base[Index];
		}

		public T RawBy(long Id)
		{
			for(int a = 0; a < base.Count; a++)
				if (RawAt(a).Id == Id)
					return RawAt(a);

			return default(T);
		}

		public void Preallocate(int NumberOfNew)
		{
		}

		#region Nested type: AlterType

		protected enum AlterType
		{
			OnCreate,
			OnUpdate,
			OnDelete
		}

		#endregion
	}
}