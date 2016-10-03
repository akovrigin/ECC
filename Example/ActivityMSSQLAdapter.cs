// --------------------------------------------------------------------
// Copyright (C) 2003-2010 The Mechanical Frog Project
// http://www.mfcom.ru
// --------------------------------------------------------------------
using System;
using System.Data;
using Mfcom.Core.DataAccess;
using Mfcom.Core.DataAccess.SqlFormater;

namespace Mfcom.Cactus809.Core.Adapters
{
	public class ActivityMSSQLAdapter : MSSQLAdapter, IActivityAdapter, IActivityIntervalAdapter
	{
		#region IActivityAdapter Members

	        public DataTable Select(long? Id, long? UserId, DateTime? Dt, string WindowCaption, string ModuleName)
        	{
	            return ExecuteSelectCommand(
        	        CommandType.Text,
                	String.Format("{0}v_{1}", SelectClause, GetTableName()),
	                new BaseSqlCondition[]
        	        {
                	    new SimpleSqlCondition<long?>("Id", Id),
	                    new SimpleSqlCondition<long?>("UserId", UserId),
        	            new SimpleSqlCondition<DateTime?>("Dt", Dt),
                	    new SimpleSqlCondition<string>("WindowCaption", WindowCaption),
	                    new SimpleSqlCondition<string>("ModuleName", ModuleName),
        	        }
                	);
	        }
		#endregion IActivityAdapter Members

		protected override string GetTableName()
		{
			return "Activity";
		}

	}
}