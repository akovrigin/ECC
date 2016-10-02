// --------------------------------------------------------------------
// Copyright (C) 2003-2010 The Mechanical Frog Project
// http://www.mfcom.ru
// --------------------------------------------------------------------
using System;
using System.Data;
using Mfcom.Core.DataAccess.SqlFormater;

namespace Mfcom.Core.DataAccess
{
	public interface IDBMSDriver
	{
		char Apostrophe{get;}
		char ParameterPrefix{get;}
		char Wildcard{get;}
		IDbConnection GetConnection();
		IDbCommand GetCommand(string ProcedureName, IDbConnection Connection);
		IDbDataParameter GetParameter();
		void DeriveParameters(IDbCommand Command);

		string FormatDateAsTs(DateTime ADate);
	}

	public abstract class AbstractDriver: IDBMSDriver
	{
		public abstract bool EnableSquareBrackets{get;}

		#region IDBMSDriver Members

		public abstract IDbConnection GetConnection();
		public abstract IDbCommand GetCommand(string ProcedureName, IDbConnection Connection);
		public abstract IDbDataParameter GetParameter();
		public abstract void DeriveParameters(IDbCommand Command);

		public abstract char Apostrophe{get;}
		public abstract char ParameterPrefix{get;}

		public abstract char Wildcard{get;}

		public abstract string FormatDateAsTs(DateTime ADate);

		#endregion

		public abstract SqlClauseFormatter CreateFormatter();
	}
}