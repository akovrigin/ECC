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
	/// ��������� ������ ��������������� ��� ���������, ������� ������������� � ������� ��������.
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
	/// ������ ���� ���������. �������� ������ ��� ���������� ��������, ��������� �������������
	/// � �������� ������ � ����������
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
		/// �������������/���������� ������ �� ��� ������� � ������ �������� ��������. �� �������, ������� �������� ���������,
		/// ������ ������������ �� ������������. ������� ������ - 0.
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
		/// ����� ���������� ��������� � ���������
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
		/// �������� ������� ��� ��������, �� ������ ��� �� ���������
		/// </summary>
		/// <param name="Index">������ ����������� ��������</param>
		public void Delete(int Index)
		{
			T item = this[Index];

			BeforeDelete(Index);
			item.IsDeleted = true;
		}

		/// <summary>
		/// � ����������� �� �������� ��������� ����� ������-������� BaseItem ���������, ������� 
		/// ��� �������� ��������������� ������ �� ������� ���������
		/// </summary>
		/// <returns>
		/// ���������� SUCCESS_CODE ���� ��� �������� � ��������� ������� ����������
		/// � ������ ������������� ������ ���������� ����� �������� ���������, �� �������
		/// ��������� ����. ������������� ���������� ��� ����������, ��������� � �������� ������
		/// ������. � ��� ����� � ���������� ���������� ���������, ����., ADO
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
				Debug.Assert(m_Adapter != null, String.Format("������� �� ������������������ ��� ����: {0}", GetType().FullName));
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
					// ��� ����������� �� ����, ��� ������� ��������� ������� � �������� - ������� ��, ��������� ��, ��� ��������,
					// �� ������ ������
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
		/// ���������� ��������� � ���������. ��������, ���������� ��������� - �� �����������
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
		/// ��������� ������� ���������
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
		/// ������ ������� ���������
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
		/// ��������� ��������� "�����" ������ �� "������", �� ����, �� ����������� �������
		/// ����������� ���������� � ������ ���������� � �������� ��������
		/// </summary>
		/// <param name="Index">���������� ������</param>
		/// <returns>���������� (�����) ������ � ������ ��������� �������� ��������</returns>
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
		/// �������� ������ � ����� ���������
		/// </summary>
		/// <param name="Value">������</param>
		/// <returns>������, ������� ����� ������ � ��������� ����� �������</returns>
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
		/// �������� ������� � ���������
		/// </summary>
		/// <param name="Index">������ �������</param>
		/// <param name="Value">������, ���������� �������</param>
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
		/// �������� � ��������� ��������� ��������� �� ���� �����
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
		/// ���������� ��� ������� BackdraftCollection ��� ���������� ������������ ��������� �� �������� ��������
		/// </summary>
		public virtual void BeforeDelete(int Index)
		{
		}

		/// <summary>
		/// �������� ��� ������� � ��������� ���������
		/// </summary>
		public void DeleteAll()
		{
			lock(SyncRoot)
			{
				int cnt = Count; // ���������� ���������� �� ������ ��������. 
				// ������������ ����� RawCount ���������������, ��� ��� �� �������� �������� � �� ��������
				for(int a = 0; a < cnt; a++) // BugzID: 920
					Delete(0);
			}
		}

		/// <summary>
		/// ������� ������� � ������ id � ��������� � �������� ��� ��������� value.
		/// ���������� ������ ��������.
		/// �������� ����� ����������� ��� ��������� ����������� ����� �������� ������ ������������� ��������.
		/// </summary>
		/// <param name="Value"></param>
		public int SetItem(T Value)
		{
			int Index = FindIndexById(Value.Id);
			base[Index] = Value;
			return Index;
		}

		/// <summary>
		/// ������� ������� � �������� �������� � ��������� � �������� ��� ��������� value.
		/// �������� ����� ����������� ��� ��������� ����������� ����� �������� ������ ������������� ��������.
		/// </summary>
		/// <param name="Value"></param>
		protected void SetItem(int Index, T Value, bool Dummy)
		{
			base[Index] = Value;
		}

		/// <summary>
		/// ����������, ����� ���������� ������� ������� �� ���������.
		/// </summary>
		/// <param name="Id">���������� ����� ������� (Id)</param>
		/// <returns>� ������ ������ ���������� true, ����� - false</returns>
		/// <remarks>Id - ���� ������� � ��</remarks>
		protected virtual bool DeleteItem(long Id)
		{
			return m_Adapter.Delete(Id) > 0;
		}

		/// <summary>
		/// ����������, ����� ���������� ������� ������� �� ���������.
		/// </summary>
		/// <param name="Id">���������� ����� ������� (Id)</param>
		/// <returns>� ������ ������ ���������� true, ����� - false</returns>
		/// <remarks>Id - ���� ������� � ��</remarks>
		private bool DeleteItemInTrans(long Id)
		{
			bool Result = true;

//			m_Adapter.BeginTransaction();

			try
			{
				// ����� ����� ���������� false ���� �� ���� ���������� �� ����� ������ - 
				// ��������, �� ������� ��� ������� ������� �� ��������.

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
					ReportError(e, "��������");
				else
					throw new UserException("��������� ���������� ������ ��� ������ � ��!", e);
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
				DeveloperError = String.Format("������ {0} �� ��� �������� � ��.", GetType().Name);

			throw new UserException(
				String.Format("��������� ������ ��� ������� {0} ������� � ��!", Action),
				DeveloperError);
		}

		/// <summary>
		/// ����������� �����. ����������, ����� ���������� �������� ������� � ���������.
		/// </summary>
		/// <param name="Index">���������� ����� �������� � ���������</param>
		/// <returns>� ������ ������ ���������� true, ����� - false</returns>
		protected virtual bool UpdateItem(int Index)
		{
			return false;
		}

		/// <summary>
		/// ���������� �����, �������������� ���������������� ��� ���������� ������� ��������.
		/// ����������, ����� ���������� �������� ������� � ���������.
		/// </summary>
		/// <param name="Index">���������� ����� �������� � ���������</param>
		/// <returns>� ������ ������ ���������� true, ����� - false</returns>
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
		/// ����������� �����. ����������, ����� ���������� ������� ������� � ���������.
		/// </summary>
		/// <param name="Index">���������� ����� �������� � ���������</param>
		/// <returns>� ������ ������ ���������� true, ����� - false</returns>
		protected virtual bool CreateItem(int Index)
		{
			return false;
		}

		/// <summary>
		/// ���������� �����, �������������� ���������������� ��� �������� ������� ��������.
		/// ����������, ����� ���������� ������� ������� � ���������.
		/// </summary>
		/// <param name="Index">���������� ����� �������� � ���������</param>
		/// <returns>� ������ ������ ���������� true, ����� - false</returns>
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

			string pattern = "��������� {0}������ ��� {1}������ {2}� ��: {3}";

			if (result)
			{
				string developerMsg;

				if (e is DatabaseException)
					developerMsg = e.InnerException.Message;
				else if (e is UserException)
					developerMsg = e.Message;
				else
					developerMsg = String.Format("������ {0} �� ��� �������� � ��.", GetType().Name);

				string msg = String.Format(pattern, "", "������� ", "������� ", e.Message);

				throw new UserException(msg, developerMsg);
			}
			else
			{
				string msg = String.Format(pattern, "���������� ", "", "", e.Message);

				throw new UserException(msg, e);
			}
		}

		/// <summary>
		/// �������� ������� �� ��������� �� ��� ��������������(�����), ����������� � �������
		/// </summary>
		/// <param name="Id">�������� ��������������(�����)</param>
		/// <returns>���������� ��������� ������, ���������� � ���� BaseItem ��� ���������� null, ���� ������ �� ������� �����</returns>
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
		/// ������� ������� �� ��������� �� Id
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
				throw new UserException(String.Format("�� ������ Id ���������� �������. Id = {0}", Id),
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
		/// ���������� ������� �� ��� "������" �������, ��� ����� ���������,
		/// ����������, ��� ��������
		/// </summary>
		/// <param name="Index">����� ������ ��������</param>
		/// <returns>������, ��������������� ������� ��� ����������, ���� ������ �� 
		/// �������� ��������������</returns>
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