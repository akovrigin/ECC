// --------------------------------------------------------------------
// Copyright (C) 2003-2010 The Mechanical Frog Project
// http://www.mfcom.ru
// --------------------------------------------------------------------
using System;
using System.Collections;
using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Serialization;
using Mfcom.Core.Attributes;
using Mfcom.Core.Base;
using Mfcom.Core.DataAccess.SqlFormater;
using Mfcom.Core.Replication;
using Mfcom.Core.Utils;
using Mfcom.Core.Utils.Exceptions;
using Mfcom.Replication;

namespace Mfcom.Core.DataAccess
{
	/// <summary>
	/// ������������ �������� �������� ������� ��� �������� (Update, Insert, Delete) � �������� ����� ��� ������� (Select)
	/// </summary>
	[Flags]
	public enum AdapterOperations
	{
		Insert = 0x1,
		Update = 0x2,
		Select = 0x4,
		Delete = 0x8,
		None = 0x10
	}

	[Serializable]
	public abstract class GenericDatabaseAdapter: IDataStorageAdapter
	{
		//private static readonly AsyncReplicator m_AsyncReplicator = new AsyncReplicator();
		private static readonly object m_CommitSync = new object();
		private static readonly object m_InitSync = new object();
		private static readonly object m_Lock = new object();
		private readonly Hashtable m_Cache = Hashtable.Synchronized(new Hashtable());
		protected IDbDataAdapter m_DataAdapter;
		protected string m_DeleteCommandText = "";
		protected string m_InsertCommandText = "";

		protected ArrayList m_StoredParameters;
		protected string m_StoredText = "";
		protected CommandType m_StoredType = CommandType.Text;
		protected object m_Sync = new object();

		private int m_TopN;
		protected string m_UpdateCommandText = "";

		protected GenericDatabaseAdapter(SerializationInfo Info, StreamingContext Context)
		{
			Info.SetType(GetType());
			MemberInfo[] mis = FormatterServices.GetSerializableMembers(GetType());

			for(int a = 0; a < mis.Length; a++)
			{
				if (mis[a].Name.ToLower() == "MarshalByRefObject+__identity".ToLower())
					continue;

				var fi = (FieldInfo)mis[a];
				fi.SetValue(this, Info.GetValue(fi.Name, fi.FieldType));
			}

			if (LegacyBehavior)
			{
				InitProcedureNames();
				InitProcedureParameters();
			}
		}

		protected GenericDatabaseAdapter()
		{
			InitAdapter();

			if (LegacyBehavior)
			{
				InitProcedureNames();
				InitProcedureParameters();
			}
		}

		#region Abstract methods and properties

		/// <summary>
		/// �������� �������, � ������� �������� �������� �� �������. 
		/// ������ ��������������� � ������������ ����� ������������
		/// </summary>
		protected abstract string GetTableName();

		/// <summary>
		/// ������� �������������� ������� (SqlDataAdapter, OracleDataAdapter)
		/// </summary>
		/// <returns></returns>
		protected abstract IDbDataAdapter CreateAdapter();

		/// <summary>
		/// �������� ������ ��� ������������ �����, ����������� ��� ��������
		/// </summary>
		/// <returns></returns>
		public abstract char GetApostropheSymbol();

		/// <summary>
		/// �������� ������ ��� ����������, ����������� ��� ��������
		/// </summary>
		/// <returns></returns>
		public abstract char GetParameterPrefix();

		/// <summary>
		/// ��������� ������� �������, ��������� ������, ����������� ��� ������� ��������
		/// </summary>
		/// <param name="table"></param>
		protected abstract void FillInTable(DataTable table);

		protected abstract SortedList PopulateSortedList();

		/// <summary>
		/// ���������� �������� ��� ��� ���������� ���������
		/// </summary>
		/// <param name="Command">�������, � ��������� �������� ��������� ��������, ����������� � ���������</param>
		/// <param name="Name">��� ���������</param>
		/// <param name="Value">��������, ������� ���� ���������</param>
		protected abstract void SetupParameter(IDbCommand Command, string Name, object Value);

		#endregion Abstract methods and properties

/*		
 *		public static AsyncReplicator AsyncReplicator
		{
			get
			{
				lock(m_Lock)
					return m_AsyncReplicator;
			}
		}
 */

#warning �� ����� �������� � ORACLE. ��������� ������ ��������� ROWNUM
		public string SelectClause
		{
			get
			{
				return (m_TopN > 0)
				       	? String.Format("select Top {0} * from ", m_TopN)
				       	: "select * from ";
			}
		}

		public IDbDataAdapter SQLDataAdapter
		{
			get
			{
				if (m_DataAdapter == null)
				{
					InitAdapter();

					m_DataAdapter.InsertCommand.CommandText = m_InsertCommandText;
					m_DataAdapter.UpdateCommand.CommandText = m_UpdateCommandText;
					m_DataAdapter.DeleteCommand.CommandText = m_DeleteCommandText;

					InitProcedureParameters();
				}

				return m_DataAdapter;
			}
		}

		/// <summary>
		/// ���������� ����� �������� � ������ ����������
		/// </summary>
		public int UniqueId
		{
			get {return ThreadId.Value;}
		}

		/// <summary>
		/// ��������� ������������ ������ ����������� ������� � ������������. ������� ���������,
		/// ����� � ������������ ������� ����������� ������ �� ����������.
		/// </summary>
		protected bool LegacyBehavior
		{
			get {return AllowVirtualsInConstructor;}
		}

		protected virtual bool AllowVirtualsInConstructor
		{
			get {return true;}
		}

		#region IDataStorageAdapter Members

		/// <summary>
		/// ���������� �����, ���������� �������� �� ��������� (select Top X * from table)
		/// </summary>
		public void SetTopN(int Count)
		{
			m_TopN = Count;
		}

		public void BeginTransaction()
		{
			GenericConnection.BeginTransaction(SQLDataAdapter, UniqueId);
		}

		public void EndTransaction()
		{
			GenericConnection.EndTransaction(UniqueId);
		}

		public void RollBack()
		{
			GenericConnection.RollBack(UniqueId);
		}

		public bool IsInTransaction()
		{
			return GenericConnection.IsInTransaction(UniqueId);
		}

		public virtual DataTable Select()
		{
			return Select(true, true);
		}

		public virtual DataTable Select(long Id)
		{
			return
				ExecuteSelectCommand(CommandType.Text, String.Format("SELECT * FROM v_{0}", GetTableName()),
				                     new BaseSqlCondition[] {new SimpleSqlCondition<long>("Id", Id, SimpleSqlOperator.Equal)});
		}

		public virtual int Count()
		{
			return Convert.ToInt32(ExecuteSelectScalar(String.Format("SELECT COUNT(1) FROM v_{0}", GetTableName())));
		}

		public virtual long Insert(BaseItem Item)
		{
			IDataParameterCollection p = SQLDataAdapter.InsertCommand.Parameters;

			SetParameters(p, Item, AdapterOperations.Insert);

			SetupIdParameter(p);

			Insert();

			return Item.Id = Convert.ToInt64(((IDataParameter)p[Prefixed("Id")]).Value);
		}

		public virtual int Update(BaseItem Item)
		{
			IDataParameterCollection p = SQLDataAdapter.UpdateCommand.Parameters;

			SetParameters(p, Item, AdapterOperations.Update);

			int count = Update();

			return count;
		}

		public virtual int Delete(long Id)
		{
			((IDataParameter)SQLDataAdapter.DeleteCommand.Parameters[Prefixed("Id")]).Value = Id;

			int count = Delete();

			return count;
		}

		#endregion

		public virtual void Init()
		{
			InitProcedureNames();
			InitProcedureParameters();
		}

		public void InitializeSelectCommandParameters()
		{
			lock(m_Sync)
			{
				try
				{
					InitCommandParameters(SQLDataAdapter.SelectCommand);
				}
				catch(Exception exc)
				{
					DumpBrokenSp(SQLDataAdapter.SelectCommand.CommandText, exc);
				}
			}
		}

		public void InitProcedureParameters()
		{
			lock(m_Sync)
			{
				Debug.Assert(SQLDataAdapter != null, "������������ ��������� � �������� '�������'");

				m_InsertCommandText = SQLDataAdapter.InsertCommand.CommandText;
				m_UpdateCommandText = SQLDataAdapter.UpdateCommand.CommandText;
				m_DeleteCommandText = SQLDataAdapter.DeleteCommand.CommandText;

				InitCommandParameters(SQLDataAdapter.UpdateCommand);
				InitCommandParameters(SQLDataAdapter.InsertCommand);
				InitCommandParameters(SQLDataAdapter.DeleteCommand);
				InitCommandParameters(SQLDataAdapter.SelectCommand);
			}
		}

		/// <summary>
		/// ��������� �������, ����������� � SelectCommand � ��� ����, ��� ��� ����
		/// </summary>
		/// <returns></returns>
		public virtual DataTable StraightSelect()
		{
			return Select(false, false);
		}

		private DataTable Select(bool UsePresave, bool GenerateCommand)
		{
			string tname = GenerateCommand? GetTableName(): "temp_table";

			var table = new DataTable(tname);

			try
			{
				GenericConnection.Open(SQLDataAdapter, UniqueId);

				if (UsePresave)
					PreserveSelectCommand();

				if (GenerateCommand)
				{
					SQLDataAdapter.SelectCommand.CommandType = CommandType.Text;
					SQLDataAdapter.SelectCommand.CommandText = String.Format("SELECT * FROM v_{0}", GetTableName());
				}

				FillInTable(table);

				if (UsePresave)
					RestoreSelectCommand();
			}
			catch(Exception ex)
			{
				DumpException(ex, SQLDataAdapter.SelectCommand.CommandText);
			}
			finally
			{
				GenericConnection.Close(UniqueId);
			}

			return table;
		}

		// ��� ����������� ������������� � ���� ������

		public virtual object SelectScalar()
		{
			int threadId = UniqueId;
			object result = null;
			try
			{
				GenericConnection.Open(SQLDataAdapter, threadId);
				result = SQLDataAdapter.SelectCommand.ExecuteScalar();
			}
			catch(Exception ex)
			{
				DumpException(ex, SQLDataAdapter.SelectCommand.CommandText);
			}
			finally
			{
				GenericConnection.Close(threadId);
			}

			return result;
		}

		/// <summary>
		/// ����� ���������� ���� �������� ��������� ����� select.
		/// ��� ������������� �������� ��������� ��������� �������� ����� ���������� ����� ��������� select � ������ ���������.
		/// ������� ������� ��������, ��� �������� @RETURN_VALUE �� ����� ���������� ��� ���������� ��������.
		/// </summary>
		/// <param name="CmdType"></param>
		/// <param name="CommandText"></param>
		/// <param name="Conditions"></param>
		/// <returns></returns>
		public virtual object ExecuteSelectScalar(CommandType CmdType, string CommandText, BaseSqlCondition[] Conditions)
		{
			SQLDataAdapter.SelectCommand.CommandType = CmdType;
			SQLDataAdapter.SelectCommand.CommandText = CommandText;

			if (CmdType == CommandType.StoredProcedure)
			{
				InitializeSelectCommandParameters();
				SetupParametersValues(SQLDataAdapter.SelectCommand, Conditions);
			}
			else
			{
				string where =
					DriversContainer.ActiveDriver.CreateFormatter().WhereClause(SQLDataAdapter.SelectCommand.Parameters, Conditions);

				if (String.IsNullOrEmpty(where))
					if (Conditions != null)
						throw new ArgumentException("Arguments are all absent");
					else
						where = ""; // ���� where 1 = 0. ��� �� ������� ��� ���������� �������� � ���������� ��� �������

				SQLDataAdapter.SelectCommand.CommandText += where;
			}

			object res = SelectScalar();

			return res == DBNull.Value? null: res;
		}

		/// <summary>
		/// ��������� SelectCommand �� ����������.
		/// ���������� ���������� ������������ �����.
		/// </summary>
		/// <returns></returns>
		public int ExecuteSelectNonQuery()
		{
			int affected = 0;
			int threadId = UniqueId;
			try
			{
				GenericConnection.BeginTransaction(SQLDataAdapter, threadId); // Open();
				lock(m_CommitSync)
					affected = SQLDataAdapter.SelectCommand.ExecuteNonQuery();
			}
			catch(Exception ex)
			{
				DumpException(ex, SQLDataAdapter.SelectCommand.CommandText);
			}
			finally
			{
				GenericConnection.EndTransaction(threadId); //Close();
			}

			return affected;
		}

		public virtual int Insert()
		{
			int affected = 0;
			int threadId = UniqueId;
			try
			{
				DumpParameters();

				GenericConnection.BeginTransaction(SQLDataAdapter, threadId); // Open();
				affected = SQLDataAdapter.InsertCommand.ExecuteNonQuery();
			}
			catch(Exception ex)
			{
				DumpException(ex, SQLDataAdapter.InsertCommand.CommandText);
			}
			finally
			{
				GenericConnection.EndTransaction(threadId); //Close();	
			}

			return affected;
		}

		private void DumpParameters()
		{
/*			Trace.WriteLine("====================  " + GetType().Name + "  =====================");
			for(int a = 0; a < SQLDataAdapter.InsertCommand.Parameters.Count; a++)
			{
				IDataParameter param = (IDataParameter)SQLDataAdapter.InsertCommand.Parameters[a];
				Trace.WriteLine(String.Format("{0} {1}", param.ParameterName, param.Value));
			}*/
		}

		public virtual int Update()
		{
			int affected = 0;
			int threadId = UniqueId;
			try
			{
				GenericConnection.BeginTransaction(SQLDataAdapter, threadId); // Open();
				affected = SQLDataAdapter.UpdateCommand.ExecuteNonQuery();
			}
			catch(Exception ex)
			{
				DumpException(ex, SQLDataAdapter.UpdateCommand.CommandText);
			}
			finally
			{
				GenericConnection.EndTransaction(threadId); //Close();
			}
			return affected;
		}

		public virtual int Delete()
		{
			int affected = 0;
			int threadId = UniqueId;
			try
			{
				GenericConnection.BeginTransaction(SQLDataAdapter, threadId); // Open();
				affected = SQLDataAdapter.DeleteCommand.ExecuteNonQuery();
			}
			catch(Exception ex)
			{
				DumpException(ex, SQLDataAdapter.DeleteCommand.CommandText);
			}
			finally
			{
				GenericConnection.EndTransaction(threadId); //Close();
			}

			return affected;
		}

		/// <summary>
		/// ���������� DataTable �� ����������� SQL-�������.
		/// </summary>
		/// <param name="TableName"></param>
		/// <param name="CommandText"></param>
		/// <returns></returns>
		public DataTable ExecuteSelectCommand(string TableName, string CommandText)
		{
			SQLDataAdapter.SelectCommand.CommandType = CommandType.Text;
			SQLDataAdapter.SelectCommand.CommandText = CommandText;

			SQLDataAdapter.SelectCommand.Parameters.Clear();

			DataTable dt = SelectQuery(TableName);

			return dt;
		}

		/// <summary>
		/// ���������� DataTable �� ����������� SQL-�������.
		/// </summary>
		/// <param name="CommandText">�������, ������� ���� ���������</param>
		/// <returns></returns>
		public DataTable ExecuteSelectCommand(string CommandText)
		{
			return ExecuteSelectCommand("Temp", CommandText);
		}

		/// <summary>
		/// ��������� ������ ��� �������� ��������� � ���������� ����� ������.
		/// ���������� ������� �������� - {ParamName, Value}
		/// FieldName - string; Value - ����� ������� ���� ������
		/// Sample : ExecuteStoredProc("sp_GetAddressGNIById", new object[,] {{"Id", Id}, {"Name", Name}});
		/// ���� �������� � sp ��������������, �� ��� ����� �������� ������ ���� �������� � ��� ���������,
		/// ��� ��� SqlAdapter ��������� ���������, �� ������-�� �� ����� ���������������� ������������ ��������.
		/// ���� � �������� ��������� ���� ������������ ��������, �� ��� ����� �������� ����� ������ ������ ����� �������:
		/// OracleSQLDataAdapter.SelectCommand.Parameters[Prefixed("Correct")].Value)
		/// </summary>
		/// <param name="CmdType">��� �������</param>
		/// <param name="Conditions">�������� ����������</param>
		/// <param name="CommandText">��� ��������� ��� �����, ������� ���� ���������</param>
		/// <returns>DataTable</returns>
		public DataTable ExecuteSelectCommand(CommandType CmdType, string CommandText, BaseSqlCondition[] Conditions)
		{
			return ExecuteSelectCommand(CmdType, "Temp", CommandText, Conditions, null);
		}

		public DataTable ExecuteSelectCommand(CommandType CmdType, string TableName, string CommandText,
		                                      BaseSqlCondition[] Conditions)
		{
			return ExecuteSelectCommand(CmdType, TableName, CommandText, Conditions, null);
		}

		public DataTable ExecuteSelectCommand(CommandType CmdType, string CommandText, BaseSqlCondition[] Conditions,
		                                      SqlOrder[] Orders)
		{
			return ExecuteSelectCommand(CmdType, "Temp", CommandText, Conditions, Orders);
		}

		public DataTable ExecuteSelectCommand(CommandType CmdType, string TableName, string CommandText,
		                                      BaseSqlCondition[] Conditions,
		                                      SqlOrder[] Orders)
		{
			SQLDataAdapter.SelectCommand.CommandType = CmdType;
			SQLDataAdapter.SelectCommand.CommandText = CommandText;

			if (CmdType == CommandType.StoredProcedure)
			{
				InitializeSelectCommandParameters();
				SetupParametersValues(SQLDataAdapter.SelectCommand, Conditions);
			}
			else if (Conditions != null)
			{
				//string where = WhereFormatter.GenerateWhereCondition(this, SQLDataAdapter.SelectCommand.Parameters, Parameters);
				//where += WhereFormatter.AddOrderByCondition(OrderByFields);

				SqlClauseFormatter formater = DriversContainer.ActiveDriver.CreateFormatter();
				string where = formater.WhereClause(SQLDataAdapter.SelectCommand.Parameters, Conditions);
				where += formater.OrderClause(Orders);

				if (String.IsNullOrEmpty(where))
					where = " where 1 = 0";

				SQLDataAdapter.SelectCommand.CommandText += where;
			}

			DataTable dt = SelectQuery(TableName);

			return dt;
		}

		/// <summary>
		/// ���������� ������ ���� ��������.
		/// � ������, ���� ������ ������ �� ��������� ��� ��������� System.DBNull, �� ������� ��������� null.
		/// </summary>
		/// <param name="CommandText">�������, ������� ���� ���������. ��� ����� SQL ��� ��� ���������</param>
		/// <returns></returns>
		public object ExecuteSelectScalar(string CommandText)
		{
			return ExecuteSelectScalar(CommandText, false);
		}

		/// <summary>
		/// ���������� ������ ���� ��������. 
		/// � ������, ���� ������ ������ �� ���������, �� ������� ��������� null.
		/// ���� ������� NullAlsoExpected == true, �� ������� ��������� System.DBNull, ���� ������ ��������� �������� NULL.
		/// </summary>
		/// <param name="CommandText">�������, ������� ���� ���������</param>
		/// <param name="NullAlsoExpected">�������� �� ������ ����� ������� ��� ������ ��� ����������</param>
		/// <returns></returns>
		public object ExecuteSelectScalar(string CommandText, bool NullAlsoExpected)
		{
			object result = ExecuteSelectScalar(CommandType.Text, CommandText, null);

			if (result != null && !NullAlsoExpected && result == DBNull.Value)
				result = null;

			return result;
		}

		public int ExecuteStoredProc(string Name, BaseSqlCondition[] Conditions)
		{
			SQLDataAdapter.SelectCommand.CommandType = CommandType.StoredProcedure;
			SQLDataAdapter.SelectCommand.CommandText = Name;

			InitializeSelectCommandParameters();
			SetupParametersValues(SQLDataAdapter.SelectCommand, Conditions);

			int AffectedRows = ExecuteSelectNonQuery();

			return AffectedRows;
		}

		public SortedList ExecuteStoredProcAndReturnParameters(string Name, BaseSqlCondition[] Conditions)
		{
			SQLDataAdapter.SelectCommand.CommandType = CommandType.StoredProcedure;
			SQLDataAdapter.SelectCommand.CommandText = Name;

			InitializeSelectCommandParameters();
			SetupParametersValues(SQLDataAdapter.SelectCommand, Conditions);

			ExecuteSelectNonQuery();

			SortedList sortedList = PopulateSortedList();

			return sortedList;
		}

		public object ExecuteWithSingleOutput(string ProcedureName, BaseSqlCondition[] Conditions, string Returnee)
		{
			SortedList outputs = ExecuteStoredProcAndReturnParameters(ProcedureName, Conditions);
			return outputs[Returnee];
		}

		public void PreserveSelectCommand()
		{
			m_StoredType = SQLDataAdapter.SelectCommand.CommandType;
			m_StoredText = SQLDataAdapter.SelectCommand.CommandText;

			m_StoredParameters = new ArrayList();
			foreach(IDataParameter parameter in SQLDataAdapter.SelectCommand.Parameters)
			{
				m_StoredParameters.Add(parameter);
			}

			// ����� ���������� ���������� �� ���� ��������
			ClearParameters();
		}

		public void RestoreSelectCommand()
		{
			SQLDataAdapter.SelectCommand.CommandType = m_StoredType;
			SQLDataAdapter.SelectCommand.CommandText = m_StoredText;

			// ����� ��������������� ������������ ���������� - ���������� �� 
			// ����������� ��� ���������� StraightSelect
			ClearParameters();
			foreach(IDataParameter parameter in m_StoredParameters)
			{
				SQLDataAdapter.SelectCommand.Parameters.Add(parameter);
			}
		}

		public void GetObjectData(SerializationInfo Info, StreamingContext Context)
		{
			Info.SetType(GetType());

			MemberInfo[] mis = FormatterServices.GetSerializableMembers(GetType());

			for(int a = 0; a < mis.Length; a++)
			{
				if (mis[a].Name.ToLower() == "MarshalByRefObject+__identity".ToLower())
					continue;

				Info.AddValue(mis[a].Name, ((FieldInfo)mis[a]).GetValue(this));
			}
		}

		private void ClearParameters()
		{
			SQLDataAdapter.SelectCommand.Parameters.Clear();
		}

		private void SetupParametersValues(IDbCommand Command, BaseSqlCondition[] Conditions)
		{
			if (Conditions != null)
				for(int i = 0; i < Conditions.Length; i++)
				{
					var singleVal = (ISingleValue)Conditions[i];

					string paramName = Conditions[i].FieldName;
					object paramValue = singleVal.Value;

					if (paramValue != null && Function.IsEnum(paramValue))
						paramValue = (int)paramValue;

					SetupParameter(Command, paramName, paramValue);
				}
		}

		// ��������������� ����� ������ ��������. ��������� ������������ ���������� ���� � ���� �����

		/// <summary>
		/// ��������� ������� ����� ������� �������� ��������� ��� �������� ��������.
		/// </summary>
		/// <param name="ParamName"></param>
		/// <returns></returns>
		public virtual string Prefixed(string ParamName)
		{
			if (String.IsNullOrEmpty(ParamName))
				throw new UserException("�� ������ ��� ���������.");

			return ParamName;
		}

		protected virtual void SetParameters(IDataParameterCollection Params, BaseItem Instance,
		                                     AdapterOperations CurrentOperation)
		{
			PropertyInfo[] infos = Instance.GetType().GetProperties();

			foreach(PropertyInfo propertyInfo in infos)
			{
				var a = (MapFieldAttribute[])m_Cache[propertyInfo.Name];
				if (a == null)
				{
					a = (MapFieldAttribute[])propertyInfo.GetCustomAttributes(typeof(MapFieldAttribute), false);
					m_Cache.Add(propertyInfo.Name, a);
				}

				foreach(MapFieldAttribute fieldAttribute in a)
				{
					// BugzID: 557	
					if ((fieldAttribute.SkipFor & CurrentOperation) == CurrentOperation)
						continue;

					// BugzID: 558
					if (!fieldAttribute.Propagate && propertyInfo.DeclaringType != Instance.GetType())
						continue;

					if (String.IsNullOrEmpty(fieldAttribute.ParameterName))
						continue;

					const BindingFlags flags = BindingFlags.Instance | BindingFlags.GetProperty |
					                           BindingFlags.Public | BindingFlags.NonPublic;

					object val = Instance.GetType().InvokeMember(propertyInfo.Name, flags, null, Instance, null);

					val = CheckIfEnum(val);

					string valType = propertyInfo.PropertyType.Name;

					if (fieldAttribute.IsNullable)
						switch(valType)
						{
							case "Int32":
								if ((int)val == 0)
									val = DBNull.Value;
								break;
							case "Int64":
								if ((long)val == 0)
									val = DBNull.Value;
								break;
							case "Decimal":
								if ((decimal)val == NullDecimal.Value)
									val = DBNull.Value;
								break;
							case "DateTime":
								if ((DateTime)val == NullDate.Value)
									val = DBNull.Value;
								break;
							case "TimeSpan":
								if ((TimeSpan)val == NullTime.Value)
									val = DBNull.Value;
								break;
							default:
								if (val == null)
									val = DBNull.Value;
								break;
						}

					if (!fieldAttribute.DateHasTimePart && valType.Equals("DateTime") && val != DBNull.Value)
						val = ((DateTime)val).Date;
					else if (valType.Equals("TimeSpan") && val != DBNull.Value)
						val = Convert.ToDateTime(val.ToString());


					 val = PreprocessParameter(fieldAttribute.ParameterName, val);
					((IDataParameter)Params[Prefixed(fieldAttribute.ParameterName)]).Value = val;
				}
			}
		}

		protected BaseSqlCondition[] Prefixed(BaseSqlCondition[] Conditions)
		{
			for(int i = Conditions.GetLowerBound(0); i <= (Conditions.GetUpperBound(0)); i++)
				Conditions[i].FieldName = Prefixed(Conditions[i].FieldName);

			return Conditions;
		}

		protected DataTable SelectQuery(string TableName)
		{
			if (null == TableName)
				throw new NullReferenceException("TableName is null");

			var table = new DataTable(TableName);

			try
			{
				GenericConnection.Open(SQLDataAdapter, UniqueId);

				FillInTable(table);
			}
			catch(Exception ex)
			{
				DumpException(ex, SQLDataAdapter.SelectCommand.CommandText);
			}
			finally
			{
				GenericConnection.Close(UniqueId);
			}

			return table;
		}

		/// <summary>
		/// ��������� ���� ���������� �������� ��� ������� �� ������, ���� ��� �������� ��������� 
		/// ������� ��������� ���-�� ����������������.
		/// </summary>
		/// <param name="val">�������� ��������, ������������� �������</param>
		/// <param name="ParamName">��� ��������� �������� ���������</param>
		/// <returns></returns>
		protected virtual object PreprocessParameter(string ParamName, object val)
		{
			return val;
		}

		protected void InitAdapter()
		{
			lock(m_Sync)
			{
				m_DataAdapter = CreateAdapter();

				int threadId = UniqueId;

				m_DataAdapter.SelectCommand = GenericConnection.CreateCommand(threadId);
				m_DataAdapter.SelectCommand.CommandType = CommandType.Text;

				m_DataAdapter.InsertCommand = GenericConnection.CreateCommand(threadId);
				m_DataAdapter.InsertCommand.CommandType = CommandType.StoredProcedure;

				m_DataAdapter.UpdateCommand = GenericConnection.CreateCommand(threadId);
				m_DataAdapter.UpdateCommand.CommandType = CommandType.StoredProcedure;

				m_DataAdapter.DeleteCommand = GenericConnection.CreateCommand(threadId);
				m_DataAdapter.DeleteCommand.CommandType = CommandType.StoredProcedure;
			}
		}

		protected void DumpException(Exception ex, string SqlCommandText)
		{
			string innerText = ex.InnerException != null? ex.InnerException.Message + "\n": "��� �������������";

			throw new DatabaseException(String.Format("���� ��� ���������� �������: {0}.\n���������������� �������: {1}\n" +
			                                          "��������� �������: {2}", SQLDataAdapter.SelectCommand.CommandText,
			                                          ex.Message, innerText), ex);
		}

		/// <summary>
		/// ���������������� ������� �������� ���������� �� ���������
		/// </summary>
		protected virtual void InitProcedureNames()
		{
			string prefix = BaseSolutionProperties.BackwardCompatibility? "sp_": "s_";
			SQLDataAdapter.InsertCommand.CommandText = String.Format("{0}Insert{1}", prefix, GetTableName());
			SQLDataAdapter.UpdateCommand.CommandText = String.Format("{0}Update{1}", prefix, GetTableName());
			SQLDataAdapter.DeleteCommand.CommandText = String.Format("{0}Delete{1}", prefix, GetTableName());
		}

		/// <summary>
		/// �������� � ���������� ���������� � ����. 
		/// </summary>
		/// <param name="BrokenProcedureName">��� ���������, �� ������� �������� ����</param>
		/// <param name="Exc">����������-���������</param>
		private static void DumpBrokenSp(string BrokenProcedureName, Exception Exc)
		{
			string innerText = Exc.InnerException != null? Exc.InnerException.Message + "\n": "<��� ����������>";

			throw new DatabaseException(String.Format("���������� ��� ������������� ���������� ��������� � MSSQLAdapter\n" +
			                                          "�������� ���������: {0}.\n�������: {2}\n����� ����, �������� ��������� ����������: {1}",
			                                          BrokenProcedureName, innerText, Exc.Message));
		}

		private static object CheckIfEnum(object val)
		{
			if (val is Enum)
				return Convert.ToInt32(val);

			return val;
		}

		/// <summary>
		/// ��������������� �������� ��������� Id �� ���������� ��������� �������.
		/// ������������, ���� DBMS ����� ����������� �� ������� ���������� �� �������/��������� �������
		/// ���, ��������, Microsoft Access
		/// </summary>
		/// <param name="Parameters">��������� ���������� �������</param>
		protected virtual void SetupIdParameter(IDataParameterCollection Parameters)
		{
		}

		/// <summary>
		/// ��������� ��������� ���������� ������� �������
		/// </summary>
		/// <param name="Command"></param>
		private void InitCommandParameters(IDbCommand Command)
		{
			try
			{
				if (Command.CommandType == CommandType.StoredProcedure && !String.IsNullOrEmpty(Command.CommandText))
					//Case 844 ���� ����� ������� ���������� �� ���������� m_InitSync, 
					//�� ��������� ��� ��������� ������ ����� ThreadingTest, ����� ��������� � �����������������
					lock(m_InitSync)
					{
						Procedure procedure = BaseSolutionProperties.ParameterCache.GetProcedure(Command.CommandText);

						Command.Parameters.Clear();
						procedure.Reset();

						foreach(ParameterStruct ps in procedure)
						{
							var args = new object[1];
							args[0] = ps.IsNullable;

							IDbDataParameter p = DriversContainer.ActiveDriver.GetParameter();

							p.ParameterName = ps.ParameterName;
							p.DbType = ps.DbType;
							p.Size = ps.Size;
							p.Direction = ps.Direction;

							p.GetType().InvokeMember("IsNullable",
							                         BindingFlags.SetProperty | BindingFlags.Instance |
							                         BindingFlags.Public,
							                         null, p, args);

							p.Precision = ps.Precision;
							p.Scale = ps.Scale;
							p.SourceColumn = ps.SourceColumn;
							p.SourceVersion = DataRowVersion.Current;
							Command.Parameters.Add(p);
						}
					}
			}
			catch(Exception ex)
			{
				DumpBrokenSp(Command.CommandText, ex);
			}
		}

		private static bool Preprocess(BaseItem Item)
		{
			// ��������������� ������� ����, ������ ���� ������
			// �� ������ ������ � ��������. �������, �.�., ���� ���
			// *���� �����������* ������. BugzID: 438
			if (Item.ReplicateVia == SqlAction.Delete ||
			    Item.ReplicateVia == SqlAction.Insert ||
			    Item.ReplicateVia == SqlAction.Update)
				return false;

			return true;
		}
	}
}