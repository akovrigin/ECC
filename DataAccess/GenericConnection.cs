// --------------------------------------------------------------------
// Copyright (C) 2003-2010 The Mechanical Frog Project
// http://www.mfcom.ru
// --------------------------------------------------------------------
#undef CASE699
#undef LOGCONNECTIONCOUNTER

using System;
using System.Collections;
using System.Data;
using Mfcom.Core.Utils;

namespace Mfcom.Core.DataAccess
{
	public class GenericTAC // TAC is [Transaction and Connection]
	{
		private IDbConnection m_Connection;
		private IDbTransaction m_Transaction;
		private int m_TransactionCounter;

		public GenericTAC(IDbConnection Connection)
		{
			m_Connection = Connection;
		}

		public int TransactionCounter
		{
			get {return m_TransactionCounter;}
			set {m_TransactionCounter = value;}
		}

		public IDbConnection Connection
		{
			get {return m_Connection;}
			set {m_Connection = value;}
		}

		public IDbTransaction Transaction
		{
			get {return m_Transaction;}
			set
			{
				m_Transaction = value;
				Inc();
			}
		}

		public void Inc()
		{
			m_TransactionCounter += 1;
		}

		public void Dec()
		{
			if (m_TransactionCounter == 1)
			{
				m_Transaction.Commit();
				m_Connection.Close();
#if LOGCONNECTIONCOUNTER
				Trace.WriteLine("CONNCLOS");
#endif
				m_TransactionCounter = 0;
			}
			else if (m_TransactionCounter > 0)
				m_TransactionCounter -= 1;
		}

		public void Rollback()
		{
			if (m_TransactionCounter != 0)
			{
				m_TransactionCounter = 0;
				m_Transaction.Rollback();

				m_Connection.Close();
#if LOGCONNECTIONCOUNTER
				Trace.WriteLine("CONNCLOS");
#endif
			}
		}
	}

	public abstract class GenericConnection
	{
		private static readonly object m_ConnectionLock;
		private static readonly Hashtable m_Connections;
		private static readonly object m_InnerSync = new object();
		private static readonly object m_OpenSync = new object();

		static GenericConnection()
		{
			m_Connections = Hashtable.Synchronized(new Hashtable());
			m_ConnectionLock = new object();
		}

		protected static IDbConnection CreateConnection()
		{
			IDbConnection c = DriversContainer.ActiveDriver.GetConnection();
			c.ConnectionString = BaseSolutionProperties.ConnectionString;

			return c;
		}

		private static IDbConnection GetConnection(int CallerId)
		{
			lock(m_ConnectionLock)
			{
				object bundle = GetBundle(CallerId);
#if CASE699
				DumpConnectionsVector(CallerId * 10);
#endif
				if (null == bundle)
				{
#if CASE699
						DumpConnectionsVector(CallerId * 1000);
#endif
					var tc = new GenericTAC(CreateConnection());
					m_Connections.Add(CallerId, tc);
				}
#if DEBUG
				DumpConnection();
#endif
				IDbConnection conn = ((GenericTAC)m_Connections[CallerId]).Connection;

				if (conn.State == ConnectionState.Closed)
					conn.ConnectionString = ThreadBoundDbContext.CurrentThreadContext.ConnectionString;

				return conn;
			}
		}

		private static object GetBundle(int CallingThreadId)
		{
			lock(typeof(GenericConnection))
				return m_Connections[CallingThreadId];
		}

		private static GenericTAC GetInnerObject(int AdapterUniqueId)
		{
			lock(m_InnerSync)
				return (GenericTAC)GetBundle(AdapterUniqueId);
		}

		public static bool IsInTransaction(int AdapterUniqueId)
		{
			return GetInnerObject(AdapterUniqueId).TransactionCounter > 0;
		}

		public static IDbCommand CreateCommand(int AdapterUniqueId)
		{
			return GetConnection(AdapterUniqueId).CreateCommand();
		}

		public static void BeginTransaction(IDbDataAdapter DataAdapter, int AdapterUniqueId)
		{
#if CASE699
			DumpSqlAdapter(DataAdapter, "before");
#endif
			OpenAndBegin(AdapterUniqueId);

			GenericTAC tac = GetInnerObject(AdapterUniqueId);

			SetupCommand(DataAdapter.SelectCommand, tac);
			SetupCommand(DataAdapter.UpdateCommand, tac);
			SetupCommand(DataAdapter.DeleteCommand, tac);
			SetupCommand(DataAdapter.InsertCommand, tac);
#if CASE699
			DumpConnectionsVector(AdapterUniqueId);
			DumpSqlAdapter(DataAdapter, "after");
#endif
		}

		private static void SetupCommand(IDbCommand Command, GenericTAC Tac)
		{
			if (Command != null)
			{
				Command.Transaction = Tac.Transaction;
				Command.Connection = Tac.Connection;
			}
		}

#if CASE699
		private static void DumpSqlAdapter(IDbDataAdapter Adapter, string Extra)
		{
			Logger.WriteError(string.Format("------- check adapter {0} --------", Extra));
			if (Adapter.InsertCommand != null)
			{
				Logger.WriteError(string.Format("Insert command text is {0}", Adapter.InsertCommand.CommandText));
				Logger.WriteError(string.Format("Insert command's connection state is {0}", Adapter.InsertCommand.Connection.State));
			}
			if (Adapter.UpdateCommand != null)
			{
				Logger.WriteError(string.Format("Update command text is {0}", Adapter.UpdateCommand.CommandText));
				Logger.WriteError(string.Format("Update command's connection state is {0}", Adapter.UpdateCommand.Connection.State));
			}
			if (Adapter.DeleteCommand != null)
			{
				Logger.WriteError(string.Format("Delete command text is {0}", Adapter.DeleteCommand.CommandText));
				Logger.WriteError(string.Format("Delete command's connection state is {0}", Adapter.DeleteCommand.Connection.State));
			}
		}

		private static void DumpConnectionsVector(int AdapterUniqueId)
		{
			Logger.WriteError(String.Format("---------------{0}------------------", AdapterUniqueId));
			foreach(int key in m_Connections.Keys)
				DumpConnection(key);
		}

		private static void DumpConnection(int Key)
		{
			GenericTAC tac = (GenericTAC)m_Connections[Key];

			Logger.WriteError(String.Format("Key: {0}", Key));
			Logger.WriteError(String.Format("Connection state: {0}", tac.Connection.State));
			Logger.WriteError(String.Format("Transaction count: {0}", tac.TransactionCounter));
		}
#endif

        public static void EndTransaction(int AdapterUniqueId)
		{
			lock(m_OpenSync)
			{
				GenericTAC tac = GetInnerObject(AdapterUniqueId);
				if (tac != null)
					tac.Dec();
				else
                    Logger.WriteError("There is no TAC for the given thread (@EndTrans)");
			}
		}

		public static void RollBack(int AdapterUniqueId)
		{
			lock(m_OpenSync)
			{
				GenericTAC tac = GetInnerObject(AdapterUniqueId);
				if (tac != null)
					tac.Rollback();
				else
                    Logger.WriteError("There is no TAC for the given thread (@Rollback)");

				m_Connections.Remove(AdapterUniqueId);
			}
		}

		public static void Open(IDbDataAdapter DataAdapter, int AdapterUniqueId)
		{
			OpenAndBegin(AdapterUniqueId);
			if (DataAdapter.SelectCommand != null)
				SetupCommand(DataAdapter.SelectCommand, GetInnerObject(AdapterUniqueId));
		}

		public static void Close(int AdapterUniqueId)
		{
			EndTransaction(AdapterUniqueId);
		}

		protected static void OpenAndBegin(int AdapterUniqueId)
		{
			lock(m_OpenSync)
			{
				GenericTAC tac = GetInnerObject(AdapterUniqueId);
				if (tac == null || tac.TransactionCounter == 0)
				{
					GetConnection(AdapterUniqueId).Open();
#if LOGCONNECTIONCOUNTER
				Trace.WriteLine("CONOP");
#endif
					tac = GetInnerObject(AdapterUniqueId);
					if (tac == null)
						throw new NullReferenceException("tac");

					tac.Transaction = GetConnection(AdapterUniqueId).BeginTransaction(IsolationLevel.ReadCommitted);
				}
				else
					tac.Inc();

				if (tac.Transaction == null)
					throw new InvalidProgramException("Счетчик транзакций больше нуля, но транзакция не создана!");

				if (tac.Connection.State != ConnectionState.Open)
					throw new InvalidProgramException(String.Format("X"));

				if (tac.Transaction.Connection.State != ConnectionState.Open)
					throw new InvalidProgramException("Y");
			}
		}

		private static void DumpConnection()
		{
#if DEBUG && LOGINTERNALS
			//			int[] keys = new int[m_Connections.Keys.Count];
			//			m_Connections.Keys.CopyTo(keys, 0);
			//			object[] values = new object[m_Connections.Keys.Count];
			//			m_Connections.Values.CopyTo(values, 0);
			//
			//			for(int a = 0; a < m_Connections.Count; a++)
			//			{
			//				Trace.WriteLine("Connection #" + a.ToString() + " state is " + ((GenericTAC)(values[a])).Connection.State.ToString() +
			//					" UniqueId is:" + keys[a].ToString());
			//			}
#endif
		}
	}
}