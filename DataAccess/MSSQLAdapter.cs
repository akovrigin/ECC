// --------------------------------------------------------------------
// Copyright (C) 2003-2010 The Mechanical Frog Project
// http://www.mfcom.ru
// --------------------------------------------------------------------
using System;
using System.Collections;
using System.Data;
using System.Data.SqlClient;
using System.Runtime.Serialization;

namespace Mfcom.Core.DataAccess
{
	[Serializable]
	public abstract class MSSQLAdapter: GenericDatabaseAdapter, ISerializable
	{
		/// <summary>
		/// Добавляет префикс "@" перед началом названия параметра для хранимых процедур в MSSQL
		/// </summary>
		/// <param name="ParamName"></param>
		/// <returns></returns>
		public override string Prefixed(string ParamName)
		{
			return String.Format("@{0}", base.Prefixed(ParamName));
		}

		protected override void FillInTable(DataTable table)
		{
			((SqlDataAdapter)SQLDataAdapter).Fill(table);
		}

		protected override SortedList PopulateSortedList()
		{
			var list = new SortedList();

			for(int i = 0; i < SQLDataAdapter.SelectCommand.Parameters.Count; i++)
			{
				var p = (SqlParameter)SQLDataAdapter.SelectCommand.Parameters[i];

				if (p.Direction != ParameterDirection.Input)
					list.Add(p.ParameterName, p.Value);
			}

			return list;
		}

		protected override void SetupParameter(IDbCommand Command, string Name, object Value)
		{
			((SqlCommand)Command).Parameters[Prefixed(Name)].Value = Value;
		}

		public override char GetApostropheSymbol()
		{
			return '\'';
		}

		public override char GetParameterPrefix()
		{
			return '@';
		}

		protected override IDbDataAdapter CreateAdapter()
		{
			return new SqlDataAdapter();
		}
	}
}