// --------------------------------------------------------------------
// Copyright (C) 2003-2010 The Mechanical Frog Project
// http://www.mfcom.ru
// --------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Data;
using Mfcom.Cactus809.Core.Adapters;
using Mfcom.Core.Base;

namespace Mfcom.Cactus809.Core.BusinessObjects
{
	/// 
	/// Summary description for ActivityEngine.
	/// 
	public class ActivityEngine: BaseEngine
	{
		public ActivityEngine()
		{
			m_Adapter = ActivityDSAFactory.Create();
		}

		protected ActivityCollection Make(DataTable Table)
		{
			ActivityCollection items = new ActivityCollection();
			items.Preallocate(Table.Rows.Count);

			foreach(DataRow r in Table.Rows)
				items.Add(Make<Activity>(r));

			return items;
		}

		public ActivityCollection GetAll()
		{
			DataTable dt = m_Adapter.Select();
			return Make(dt);
		}

		public ActivityCollection GetSelected(long Id)
		{
			return Make(m_Adapter.Select(Id));
		}

		public ActivityCollection GetSelected(long? Id, long? UserId, DateTime? Dt, string WindowCaption, string ModuleName)
		{
			return Make(((IActivityAdapter)m_Adapter).Select(Id, UserId, Dt, WindowCaption, ModuleName));                          
		}
		
		public ActivityCollection GetLast(long UserId)
		{
			return Make(((IActivityAdapter)m_Adapter).SelectLast(UserId));
		}			
	}
}