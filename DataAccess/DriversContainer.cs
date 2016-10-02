// --------------------------------------------------------------------
// Copyright (C) 2003-2010 The Mechanical Frog Project
// http://www.mfcom.ru
// --------------------------------------------------------------------
using System;
using System.Collections;
using Mfcom.Core.Utils;

namespace Mfcom.Core.DataAccess
{
	public static class DriversContainer
	{
		private static readonly Hashtable m_DriversHash = Hashtable.Synchronized(new Hashtable());

		private static readonly object m_InstallSync = new object();

		public static AbstractDriver ActiveDriver
		{
			get
			{
				int activeDbms = Convert.ToInt32(BaseSolutionProperties.DataStorageType);
				if (m_DriversHash == null)
					throw new NullReferenceException("Таблица драйверов пуста");

				return (AbstractDriver)m_DriversHash[activeDbms];
			}
		}

		public static void InstallDriver(int DataStorageType, AbstractDriver Driver)
		{
			lock(m_InstallSync)
			{
				if (Driver == null)
					throw new NullReferenceException("Драйвер");

				if (!m_DriversHash.ContainsKey(DataStorageType))
					m_DriversHash.Add(DataStorageType, Driver);
			}
		}
	}

	public class BaseDriverInstaller
	{
		public virtual void InstallDrivers()
		{
			DriversContainer.InstallDriver((int)DataStorageType.MSSQL, new MsSqlDriver());
			DriversContainer.InstallDriver((int)DataStorageType.Oracle, new OracleDriver());
		}
	}
}