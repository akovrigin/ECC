// --------------------------------------------------------------------
// Copyright (C) 2003-2010 The Mechanical Frog Project
// http://www.mfcom.ru
// --------------------------------------------------------------------
using System;
using System.Data;
using Mfcom.Core.Base;
using Mfcom.Core.Utils.Exceptions;

namespace Mfcom.Core.DataAccess
{
	/// <summary>
	/// Summary description for XMLAdapter.
	/// </summary>
	public abstract class XMLAdapter: IAbstractDataStorageAdapter
	{
		private readonly DataSet m_Ds = new DataSet();
		private DataTable m_CachedTable;
		private string m_Criteria = "";
		private BaseItem m_ProcessingItem;

		public XMLAdapter()
		{
			InitializeDataTable();
		}

		public string Criteria
		{
			get {return m_Criteria;}
			set {m_Criteria = value;}
		}

		public BaseItem CurrentItem
		{
			get {return m_ProcessingItem;}
			set {m_ProcessingItem = value;}
		}

		#region IAbstractDataStorageAdapter Members

		public DataTable Select()
		{
			m_CachedTable = m_Ds.Tables[GetTableName()];

			DataTable dt = m_CachedTable.Clone();

			DataRow[] rows = m_CachedTable.Select(Criteria);
			if (null != rows)
				foreach(DataRow row in rows)
					dt.ImportRow(row);

			return dt;
		}

		public int Insert()
		{
			int a = 1;

			try
			{
				DataRow row = m_Ds.Tables[GetTableName()].NewRow();

				PopulateRow(row);

				m_Ds.Tables[GetTableName()].Rows.Add(row);

				m_Ds.AcceptChanges();
				m_Ds.WriteXml(GetPathToData());

				CurrentItem = null;
			}
			catch
			{
				a = 0;
			}

			return a;
		}

		public int Update()
		{
			int a = 1;

			try
			{
				DataRow row = FindRow(m_CachedTable = m_Ds.Tables[GetTableName()]);

				PopulateRow(row);

				m_Ds.AcceptChanges();
				m_Ds.WriteXml(GetPathToData());

				CurrentItem = null;
			}
			catch
			{
				a = 0;
			}

			return a;
		}

		public int Delete()
		{
			try
			{
				DataTable t = m_Ds.Tables[GetTableName()];

				t.Rows.Remove(FindRow(t));

				m_Ds.AcceptChanges();
				m_Ds.WriteXml(GetPathToData());

				CurrentItem = null;
				return 1;
			}
			catch
			{
				return 0;
			}
		}

		public virtual DataTable Select(long Id)
		{
			throw new NotImplementedException();
		}

		#endregion

		~XMLAdapter()
		{
			//			m_Ds.WriteXml(GetPathToSchema());
			//
			//			m_Ds = null;
			//			m_ProcessingItem = null;
		}

		protected abstract string GetTableName();

		protected virtual string GetPathToSchema()
		{
			throw new NotImplementedException();
		}

		protected virtual string GetPathToData()
		{
			throw new NotImplementedException();
		}

		protected virtual DataRow FindRow(DataTable Table)
		{
			if (m_ProcessingItem == null)
				throw new NullReferenceException("Неудача при поиске");

			DataRow[] rs = Table.Select(String.Format("Id = {0}", CurrentItem.Id));
			if (rs.Length > 1)
				throw new UserException("Неудача при поиске роли");

			if (rs.Length == 1)
				return rs[0];

			return null;
		}

		protected abstract void PopulateRow(DataRow Row);

		protected virtual void InitializeDataTable()
		{
			string schemaPath = GetPathToSchema();
			string dataPath = GetPathToData();

			m_Ds.ReadXmlSchema(schemaPath);
			m_Ds.ReadXml(dataPath);
		}
	}
}