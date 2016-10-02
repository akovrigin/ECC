// --------------------------------------------------------------------
// Copyright (C) 2003-2010 The Mechanical Frog Project
// http://www.mfcom.ru
// --------------------------------------------------------------------
using System;
using System.Configuration;
using System.Data;
using Mfcom.Core.DataAccess;
using Mfcom.Core.DataAccess.SqlFormater;
using Mfcom.Core.Utils;

namespace Mfcom.Core.Adapters
{
	[Serializable]
	public class SimpleDSAFactory: DataStorageAdapterFactory
	{
		private ISimpleDbDSA m_DSA;

		private ISimpleDbDSA Create()
		{
			if (m_DSA != null)
				return m_DSA;

			if (BaseSolutionProperties.DataStorageType == DataStorageType.MSSQL)
				m_DSA = new SimpleMSSQLAdapter();

			if (BaseSolutionProperties.DataStorageType == DataStorageType.Oracle)
				m_DSA = new SimpleOracleSQLAdapter();
				
			if (BaseSolutionProperties.DataStorageType == DataStorageType.Access)
				m_DSA = new SimpleAccessAdapter();
				
			if (BaseSolutionProperties.DataStorageType == DataStorageType.XML)
				m_DSA = new SimpleXMLAdapter();

			if (m_DSA == null || BaseSolutionProperties.DataStorageType == DataStorageType.Unknown)
				throw new ConfigurationErrorsException(ResourceStringsManager.GetString("DATA_STORAGE_MISSED"));

			return m_DSA;
		}

		public DataTable ExecuteSelectCommand(string TableName, string CommandText)
		{
			return Create().Execute(CommandType.Text, TableName, CommandText, null);
		}

		public DataTable ExecuteSelectCommand(CommandType CmdType, string CommandText, BaseSqlCondition[] Conditions)
		{
			return Create().Execute(CmdType, "SimpleQuery", CommandText, Conditions);
		}

		public DataTable ExecuteSelectCommand(CommandType CmdType, string TableName, string CommandText,
		                                      BaseSqlCondition[] Conditions)
		{
			return Create().Execute(CmdType, TableName, CommandText, Conditions);
		}

		public void ExecuteStoredProc(string Name, BaseSqlCondition[] Conditions)
		{
			Create().Execute(CommandType.StoredProcedure, "SimpleQuery", Name, Conditions);
		}

		#region Nested type: ISimpleDbDSA

		public interface ISimpleDbDSA
		{
			DataTable Execute(CommandType CmdType, string TableName, string CommandText, BaseSqlCondition[] Conditions);
		}

		#endregion
	}
}