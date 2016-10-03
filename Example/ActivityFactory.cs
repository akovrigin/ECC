// --------------------------------------------------------------------
// Copyright (C) 2003-2010 The Mechanical Frog Project
// http://www.mfcom.ru
// --------------------------------------------------------------------
using System;
using System.Configuration;
using Mfcom.Core.DataAccess;
using Mfcom.Core.Utils;

namespace Mfcom.Cactus809.Core.Adapters
{
	[Serializable]
	public class ActivityDSAFactory: DataStorageAdapterFactory
	{
		private ActivityDSAFactory()
		{
		}

		public static IActivityAdapter Create()
		{
			if (BaseSolutionProperties.DataStorageType == DataStorageType.MSSQL)
				return new ActivityMSSQLAdapter();

			if (BaseSolutionProperties.DataStorageType == DataStorageType.Oracle)
				return new ActivityOracleAdapter();

			throw new ConfigurationErrorsException(ResourceStringsManager.GetString("DATA_STORAGE_MISSED"));
		}
	}
}