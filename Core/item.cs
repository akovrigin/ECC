// --------------------------------------------------------------------
// Copyright (C) 2003-2010 The Mechanical Frog Project
// http://www.mfcom.ru
// --------------------------------------------------------------------
using System;
using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Serialization;
using Mfcom.Core.Actualizers;
using Mfcom.Core.Attributes;
using Mfcom.Core.DataAccess;
using Mfcom.Replication;

namespace Mfcom.Core.Base
{
	public class UpdateStatusEventArgs
	{
		#region UpdateState enum

		public enum UpdateState
		{
			Unknown,
			Set,
			Reset
		}

		#endregion

		private readonly object m_Sender;
		private readonly UpdateState m_State;

		public UpdateStatusEventArgs(object Sender, UpdateState State)
		{
			m_State = State;
			m_Sender = Sender;
		}

		public UpdateState State
		{
			get {return m_State;}
		}

		public object Target
		{
			get {return m_Sender;}
		}
	}

	public delegate void UpdateStatusHandler(object Sender, UpdateStatusEventArgs Args);

	/// <summary>
	/// ������ �������� ���������. ������� ����� ������ �������� � 
	/// ��������������� ��������� ����������.
	/// </summary>
	[Serializable]
	public class BaseItem: IComparer,
	                       IComparable,
	                       ICloneable,
	                       IUpdateFlagsCalculator,
	                       INotifyPropertyChanged
	{
		protected static long m_JustCreatedId;

		/// <summary>
		/// ������� ����, ��� ������ ������������ � �������� �������������
		/// </summary>
		private bool m_ClassStorage;

		/// <summary>
		/// ����, ���������� �������� �������� Id
		/// </summary>
		private long m_Id = -1;

		/// <summary>
		/// ����, ���������� �������� �������� Deleted
		/// </summary>
		private bool m_IsDeleted;

		/// <summary>
		/// ����, ���������� �������� �������� Updated
		/// </summary>
		private bool m_IsUpdated;

		/// <summary>
		/// ������� ����, ��� ������ ������ ���� ��������� �� ������ ������� ������ ��� ����������� �� �������� ������. 
		/// </summary>
		private SqlAction m_ReplicateVia = SqlAction.Nothing;

		/// <summary>
		/// ��������� � ������������ ���� ����, ����� ����������� ���������
		/// ������� ������� � ���������� �� ��� �������� �� ����������
		/// </summary>
		[NonSerialized] private bool m_WasChangedInProcess;

		protected bool m_WriteToMemory;

		/// <summary>
		/// ���������������� �������������� ����� ���������� � 100000. �� ����� - ���������.
		/// </summary>
		private const int m_UserId = 100000;

		public BaseItem()
		{
			if ((m_Id = --m_JustCreatedId) > -1) // Id has been wrapped around
				Alert();

			if ((m_Id % m_UserId) == 0 && (m_Id / m_UserId) > 0) // ����������, ������� �� 100000
				Monitor();
		}

		protected BaseItem(SerializationInfo Info, StreamingContext Context)
		{
			Info.SetType(GetType());
			MemberInfo[] mis = FormatterServices.GetSerializableMembers(GetType());

			for(int a = 0; a < mis.Length; a++)
			{
				var info = (FieldInfo)mis[a];
				info.SetValue(this, Info.GetValue(info.Name, info.FieldType));
			}
		}

		public BaseItem(long Id)
		{
			m_Id = Id;
		}

		public BaseItem(BaseItem Item)
		{
			m_Id = Item.Id;
			m_IsDeleted = Item.IsDeleted;
			m_IsUpdated = Item.IsUpdated;
			m_ReplicateVia = Item.m_ReplicateVia;
			m_WasChangedInProcess = Item.m_WasChangedInProcess;
			m_WriteToMemory = Item.m_WriteToMemory;
			m_ClassStorage = Item.m_ClassStorage;
		}

		/// <summary>
		/// Id - ���������� ������������� ��������. ��� �������� ������������ ��� 
		/// ������������� �������� � ��������������� ���������. � ������ ��� ���������� 
		/// ������� ���� ������������� - �������������, ������ ��� �������� ����, ��� ������
		/// �������� ����� - ���������� ���� IsNew, � �� �������� Id
		/// </summary>
		[MapField("Id", "Id", Propagate = true)]
		public long Id
		{
			get {return m_Id;}
			set {m_Id = value;}
		}

		/// <summary>
		/// ������� ����������� � �������� �������. ��� ������ � ��������������� ��������� 
		/// ������ Update ��� ������������� ����� ����� ��������� �������� ������� �� ��������� 
		/// (��, ��������). �� ������� ��������� � ��������� �������� ������ ���������� � ��������� 
		/// � ��� �������� ����� ��������.
		/// </summary>
		public bool IsDeleted
		{
			get {return m_IsDeleted;}
			set {m_IsDeleted = value;}
		}

		/// <summary>
		/// ������� ����, ��� ������ ������� �������� � ������� ��� �������� ��� � ���������� ������
		/// ������ MakeIntact
		/// </summary>
		public bool IsUpdated
		{
			get {return m_IsUpdated;}
			set
			{
				m_IsUpdated = value;

				if (null != UpdateStateChanged)
					UpdateStateChanged(this,
					                   new UpdateStatusEventArgs(this,
					                                             value
					                                             	? UpdateStatusEventArgs.UpdateState.Set
					                                             	:
					                                             		UpdateStatusEventArgs.UpdateState.Reset));
			}
		}

		/// <summary>
		/// ������������ � �������� �������� ��������� ������ ������� � ����� 
		/// ���������� �������. ������ ������������ �������.
		/// </summary>
		public bool WasChangedInProcess
		{
			set {m_WasChangedInProcess = value;}
			get {return m_WasChangedInProcess || HasCollections() || (this is IHasCollections);}
		}

		/// <summary>
		/// ���� �� �������������� �������� �������?
		/// </summary>
		public bool IsModified
		{
			get {return IsUpdated || IsDeleted;}
		}

		/// <summary>
		/// �������� �� ������ �����, �� ������������� � ���������
		/// </summary>
		public bool IsNew
		{
			get {return m_Id <= 0;}
		}

		/// <summary>
		/// ���� ���������� � true, �� ��� ���������� ������� ���������� ����� ����� Update ������ �� ����� ����������� � ��.
		/// ��� ���� ������-�������� �� ���������
		/// </summary>
		public virtual bool DeferDBOperations
		{
			get {return m_WriteToMemory;}
			set {m_WriteToMemory = value;}
		}

		/// <summary>
		/// ��������� � ������������ ���� ���� ����� �������� ������-�������
		/// </summary>
		public bool ClassStorage
		{
			get {return m_ClassStorage;}
			set {m_ClassStorage = value;}
		}

		/// <summary>
		/// ��� ��������, ����������� ��� ��������� ������� � �������� ���������� �� ����������� �������.
		/// ������� ������������.
		/// </summary>
		public SqlAction ReplicateVia
		{
			get {return m_ReplicateVia;}
			set {m_ReplicateVia = value;}
		}

		#region ICloneable Members

		/// <summary>
		/// ������� ����� ������� �� �������
		/// </summary>
		/// <returns>����� �������</returns>
		public object Clone()
		{
			using(var stream = new MemoryStream())
			{
				//SoapFormatter formatter = new SoapFormatter();
				Type type = GetType();
				var formatter = new XmlSerializer(type);
				//BinaryFormatter formatter = new BinaryFormatter();
				formatter.Serialize(stream, this);

				stream.Position = 0;
				object clone = formatter.Deserialize(stream);

				((BaseItem)clone).WasChangedInProcess = false;

				return clone;
			}
		}

		#endregion

		#region IComparable Members

		public int CompareTo(object obj)
		{
			BaseItem _X = this;
			var _Y = obj as BaseItem;

			if (_X.m_Id < _Y.m_Id)
				return -1;

			if (_X.m_Id == _Y.m_Id)
				return 0;

			return 1;
		}

		#endregion

		#region IComparer Members

		/// <summary>
		/// ������������ ��� �������������� �������� � ��������� �� Id
		/// </summary>
		/// <param name="X"></param>
		/// <param name="Y"></param>
		/// <returns></returns>
		public int Compare(object X, object Y)
		{
			var _X = X as BaseItem;
			var _Y = Y as BaseItem;

			if (_X.m_Id < _Y.m_Id)
				return -1;

			if (_X.m_Id == _Y.m_Id)
				return 0;

			return 1;
		}

		#endregion

		#region INotifyPropertyChanged Members

		[field: NonSerialized]
		public event PropertyChangedEventHandler PropertyChanged;

		#endregion

		#region IUpdateFlagsCalculator Members

		/// <summary>
		/// ����������, ��� ����� ������ ��� ���������� ������� �������. ��������� ��������:
		/// * �������
		/// * ����������
		/// * ��������
		/// </summary>
		/// <returns>��� ����� ������ ��� ����������</returns>
		public SqlAction GetOperationType()
		{
			return HandleOwnUpdate();
		}

		#endregion

		[field: NonSerialized]
		public event UpdateStatusHandler UpdateStateChanged;

		/// <summary>
		/// �������� ��� ���������� ����� �������, ��� ����� ��������� ������� 
		/// �� �������� ����� �������� �� ��. ������ ���� IsNew ��������� ��� ���������
		/// </summary>
		public void MakeIntact()
		{
			m_IsDeleted = false;
			IsUpdated = false;
			ReplicateVia = SqlAction.Nothing;
		}

		/// <summary>
		/// �������� ������, ��� ���������� ��������� � ���������.
		/// </summary>
		public void MakeDirty()
		{
			IsUpdated = true;
			m_WasChangedInProcess = true;
		}

		/// <summary>
		/// �������� �� ������ ���������� ���������, ��������� ����������
		/// </summary>
		/// <returns></returns>
		public virtual bool HasCollections()
		{
			return false;
		}

		private void Alert()
		{
			// �������� � ���, ��� ����� ����������� �������� ������ �� ��������� 
		}

		private void Monitor()
		{
			// ����������� ���������� ��������� ������� � 100000 ��������������� ��������
		}

		/// <summary>
		/// ����� ���������� ��������� ���������, ���������� ��������, � �������� ���������� �� ������, ����
		/// ���� ������ ������� ��������� ���������� ��� ������ �� ������.
		/// </summary>
		/// <returns>��������, ������� ���������� ��������� ��� �������</returns>
		protected virtual SqlAction HandleOwnUpdate()
		{
			var _this = this as IIdDoesExist;
			bool exists = false;

			if (null != _this && !IsNew)
				exists = _this.Exists(Id);

			if (ReplicateVia == SqlAction.Nothing)
				return new DefaultUpdateFlagsCalculator().GetOperationType(this);

			if (ReplicateVia != SqlAction.Delete && exists)
				ReplicateVia = SqlAction.Update;

			if (ReplicateVia == SqlAction.Update)
				MakeDirty();

			return new DefaultUpdateFlagsCalculator().GetOperationType(this);
		}

		public virtual void CollectionIsDirty(object Sender, UpdateStatusEventArgs Args)
		{
			if (Args.State == UpdateStatusEventArgs.UpdateState.Set)
				IsUpdated = true;
			else if (Args.State == UpdateStatusEventArgs.UpdateState.Reset)
			{
				//������������ ������ ����� ���������� "�������", ���� ���� �������� ���� "������"
			}
			else
				throw new InvalidEnumArgumentException(String.Format("{0} is unexpected", Args.State));
		}

		protected void NotifyPropertyChanged(string PropertyName)
		{
			if (PropertyChanged != null)
				PropertyChanged(this, new PropertyChangedEventArgs(PropertyName));
		}
	}
}